using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;

namespace Office.Api.Common;

public enum XlsxColumnKind { Text, Link, Date, Number }

/// <param name="Width">In characters, as Excel measures a column.</param>
public record XlsxColumn(string Header, XlsxColumnKind Kind, double Width);

/// <summary>One cell. Text and links are always text — never a formula; a date is local time.</summary>
public readonly record struct XlsxCell(string? Text = null, string? Url = null, DateTime? Date = null, long? Number = null)
{
    public static XlsxCell Empty => default;
    public static XlsxCell Of(string? text) => new(Text: text);
    public static XlsxCell LinkTo(string text, string url) => new(Text: text, Url: url);
    public static XlsxCell At(DateTime? localTime) => new(Date: localTime);
    public static XlsxCell Count(long value) => new(Number: value);
}

/// <summary>
/// A one-sheet .xlsx (Office Open XML) made with what .NET already has — a zip and XML, no
/// spreadsheet library. Unlike CSV (whose separator depends on the computer's language — ";" in
/// Russian settings, "," elsewhere), it opens the same in Excel, WPS, Numbers, Google Sheets and
/// LibreOffice, on a computer or a phone.
///
/// The look: a bold header on dark blue, frozen with the first <c>frozenColumns</c> columns, a
/// filter on every column, striped rows, thin borders, real dates, landscape printing one page
/// wide. Every text is an inline string — a spreadsheet never reads it as a formula — and one
/// that looks like a formula (= + - @, tab, line break) is also marked "typed as text", so it
/// stays text even when someone edits the cell later.
/// </summary>
public static class XlsxSheet
{
    public const string ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    // Excel's limit for one cell.
    private const int MaxCellLength = 32_767;

    // Style indexes in styles.xml (cellXfs), below. A data style + 1 is the same on a striped row.
    private const int HeaderStyle = 1;
    private const int TextStyle = 2;
    private const int TextAsTypedStyle = 4;
    private const int LinkStyle = 6;
    private const int DateStyle = 8;
    private const int NumberStyle = 10;

    private const string MainNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string PackageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

    public static byte[] Build(
        string sheetName, IReadOnlyList<XlsxColumn> columns, IReadOnlyList<IReadOnlyList<XlsxCell>> rows, int frozenColumns = 1)
    {
        if (columns.Count == 0)
            throw new ArgumentException("A sheet needs at least one column.", nameof(columns));
        sheetName = SafeSheetName(sheetName);
        var lastCell = $"{ColumnName(columns.Count - 1)}{rows.Count + 1}";
        var links = new List<(string Ref, string Url)>();
        var sheet = Part(w => WriteSheet(w, columns, rows, frozenColumns, lastCell, links));

        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(zip, "[Content_Types].xml", ContentTypes);
            Add(zip, "_rels/.rels", PackageRels);
            Add(zip, "xl/workbook.xml", Part(w => WriteWorkbook(w, sheetName, lastCell)));
            Add(zip, "xl/_rels/workbook.xml.rels", WorkbookRels);
            Add(zip, "xl/styles.xml", Styles);
            Add(zip, "xl/worksheets/sheet1.xml", sheet);
            if (links.Count > 0)
                Add(zip, "xl/worksheets/_rels/sheet1.xml.rels", Part(w => WriteSheetRels(w, links)));
        }
        return output.ToArray();
    }

    /// <summary>A → 0, Z → 25, AA → 26.</summary>
    public static string ColumnName(int index)
    {
        var name = "";
        for (index++; index > 0; index = (index - 1) / 26)
            name = (char)('A' + (index - 1) % 26) + name;
        return name;
    }

    /// <summary>Only what XML 1.0 allows (a stray control character would make the file unreadable), cut to Excel's cell limit.</summary>
    public static string XmlSafe(string value)
    {
        var text = new StringBuilder(Math.Min(value.Length, MaxCellLength));
        for (var i = 0; i < value.Length && text.Length < MaxCellLength; i++)
        {
            var c = value[i];
            if (char.IsHighSurrogate(c))
            {
                if (i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]) && text.Length + 1 < MaxCellLength)
                    text.Append(c).Append(value[++i]);
                continue;
            }
            if (c is '\t' or '\n' or '\r' || (c >= 0x20 && c <= 0xD7FF) || (c >= 0xE000 && c <= 0xFFFD))
                text.Append(c);
        }
        return text.ToString();
    }

    /// <summary>What a spreadsheet would take for a formula if it were typed in.</summary>
    public static bool LooksLikeFormula(string value) =>
        value.Length > 0 && value[0] is '=' or '+' or '-' or '@' or '\t' or '\r' or '\n';

    private static void WriteSheet(
        XmlWriter w, IReadOnlyList<XlsxColumn> columns, IReadOnlyList<IReadOnlyList<XlsxCell>> rows,
        int frozenColumns, string lastCell, List<(string Ref, string Url)> links)
    {
        w.WriteStartElement("worksheet", MainNs);
        w.WriteAttributeString("xmlns", "r", null, RelNs);

        w.WriteStartElement("sheetPr");
        w.WriteStartElement("pageSetUpPr");
        w.WriteAttributeString("fitToPage", "1");
        w.WriteEndElement();
        w.WriteEndElement();

        w.WriteStartElement("dimension");
        w.WriteAttributeString("ref", $"A1:{lastCell}");
        w.WriteEndElement();

        // The header row and the first columns stay in view while scrolling.
        var frozen = Math.Clamp(frozenColumns, 0, columns.Count - 1);
        var topLeft = $"{ColumnName(frozen)}2";
        w.WriteStartElement("sheetViews");
        w.WriteStartElement("sheetView");
        w.WriteAttributeString("tabSelected", "1");
        w.WriteAttributeString("workbookViewId", "0");
        w.WriteStartElement("pane");
        if (frozen > 0)
            w.WriteAttributeString("xSplit", frozen.ToString(CultureInfo.InvariantCulture));
        w.WriteAttributeString("ySplit", "1");
        w.WriteAttributeString("topLeftCell", topLeft);
        w.WriteAttributeString("activePane", frozen > 0 ? "bottomRight" : "bottomLeft");
        w.WriteAttributeString("state", "frozen");
        w.WriteEndElement();
        w.WriteStartElement("selection");
        w.WriteAttributeString("pane", frozen > 0 ? "bottomRight" : "bottomLeft");
        w.WriteAttributeString("activeCell", topLeft);
        w.WriteAttributeString("sqref", topLeft);
        w.WriteEndElement();
        w.WriteEndElement();
        w.WriteEndElement();

        w.WriteStartElement("sheetFormatPr");
        w.WriteAttributeString("defaultRowHeight", "18");
        w.WriteEndElement();

        w.WriteStartElement("cols");
        for (var i = 0; i < columns.Count; i++)
        {
            w.WriteStartElement("col");
            w.WriteAttributeString("min", (i + 1).ToString(CultureInfo.InvariantCulture));
            w.WriteAttributeString("max", (i + 1).ToString(CultureInfo.InvariantCulture));
            w.WriteAttributeString("width", columns[i].Width.ToString("0.##", CultureInfo.InvariantCulture));
            w.WriteAttributeString("customWidth", "1");
            w.WriteEndElement();
        }
        w.WriteEndElement();

        w.WriteStartElement("sheetData");
        WriteRow(w, 1, height: "24", columns.Select((c, i) => (i, XlsxCell.Of(c.Header), HeaderStyle)));
        for (var r = 0; r < rows.Count; r++)
        {
            var striped = r % 2 == 1 ? 1 : 0;
            var rowNumber = r + 2;
            WriteRow(w, rowNumber, height: null, columns.Select((column, i) =>
            {
                var cell = i < rows[r].Count ? rows[r][i] : XlsxCell.Empty;
                var style = column.Kind switch
                {
                    XlsxColumnKind.Link when cell.Url is not null => LinkStyle,
                    XlsxColumnKind.Date => DateStyle,
                    XlsxColumnKind.Number => NumberStyle,
                    _ when cell.Text is not null && LooksLikeFormula(cell.Text) => TextAsTypedStyle,
                    _ => TextStyle,
                };
                if (cell.Url is not null)
                    links.Add(($"{ColumnName(i)}{rowNumber}", cell.Url));
                return (i, cell, style + striped);
            }));
        }
        w.WriteEndElement();

        w.WriteStartElement("autoFilter");
        w.WriteAttributeString("ref", $"A1:{lastCell}");
        w.WriteEndElement();

        if (links.Count > 0)
        {
            w.WriteStartElement("hyperlinks");
            for (var i = 0; i < links.Count; i++)
            {
                w.WriteStartElement("hyperlink");
                w.WriteAttributeString("ref", links[i].Ref);
                w.WriteAttributeString("id", RelNs, $"rId{i + 1}");
                w.WriteEndElement();
            }
            w.WriteEndElement();
        }

        w.WriteStartElement("pageMargins");
        foreach (var (name, value) in new[] { ("left", "0.4"), ("right", "0.4"), ("top", "0.5"), ("bottom", "0.5"), ("header", "0.3"), ("footer", "0.3") })
            w.WriteAttributeString(name, value);
        w.WriteEndElement();

        w.WriteStartElement("pageSetup");
        w.WriteAttributeString("orientation", "landscape");
        w.WriteAttributeString("fitToWidth", "1");
        w.WriteAttributeString("fitToHeight", "0");
        w.WriteEndElement();

        w.WriteEndElement();
    }

    private static void WriteRow(XmlWriter w, int rowNumber, string? height, IEnumerable<(int Column, XlsxCell Cell, int Style)> cells)
    {
        w.WriteStartElement("row");
        w.WriteAttributeString("r", rowNumber.ToString(CultureInfo.InvariantCulture));
        if (height is not null)
        {
            w.WriteAttributeString("ht", height);
            w.WriteAttributeString("customHeight", "1");
        }

        foreach (var (column, cell, style) in cells)
        {
            w.WriteStartElement("c");
            w.WriteAttributeString("r", $"{ColumnName(column)}{rowNumber}");
            w.WriteAttributeString("s", style.ToString(CultureInfo.InvariantCulture));
            if (cell.Date is { } date)
            {
                w.WriteElementString("v", date.ToOADate().ToString("R", CultureInfo.InvariantCulture));
            }
            else if (cell.Number is { } number)
            {
                w.WriteElementString("v", number.ToString(CultureInfo.InvariantCulture));
            }
            else if (!string.IsNullOrEmpty(cell.Text))
            {
                var text = XmlSafe(cell.Text);
                w.WriteAttributeString("t", "inlineStr");
                w.WriteStartElement("is");
                w.WriteStartElement("t");
                if (text.Length > 0 && (char.IsWhiteSpace(text[0]) || char.IsWhiteSpace(text[^1]) || text.Contains('\n')))
                    w.WriteAttributeString("xml", "space", null, "preserve");
                w.WriteString(text);
                w.WriteEndElement();
                w.WriteEndElement();
            }
            w.WriteEndElement();
        }

        w.WriteEndElement();
    }

    private static void WriteWorkbook(XmlWriter w, string sheetName, string lastCell)
    {
        w.WriteStartElement("workbook", MainNs);
        w.WriteAttributeString("xmlns", "r", null, RelNs);
        w.WriteStartElement("bookViews");
        w.WriteStartElement("workbookView");
        w.WriteEndElement();
        w.WriteEndElement();
        w.WriteStartElement("sheets");
        w.WriteStartElement("sheet");
        w.WriteAttributeString("name", sheetName);
        w.WriteAttributeString("sheetId", "1");
        w.WriteAttributeString("id", RelNs, "rId1");
        w.WriteEndElement();
        w.WriteEndElement();
        // Excel keeps the filter's range under this name.
        w.WriteStartElement("definedNames");
        w.WriteStartElement("definedName");
        w.WriteAttributeString("name", "_xlnm._FilterDatabase");
        w.WriteAttributeString("localSheetId", "0");
        w.WriteAttributeString("hidden", "1");
        w.WriteString($"'{sheetName.Replace("'", "''")}'!$A$1:${string.Concat(lastCell.TakeWhile(char.IsLetter))}${string.Concat(lastCell.SkipWhile(char.IsLetter))}");
        w.WriteEndElement();
        w.WriteEndElement();
        w.WriteEndElement();
    }

    private static void WriteSheetRels(XmlWriter w, List<(string Ref, string Url)> links)
    {
        w.WriteStartElement("Relationships", PackageRelNs);
        for (var i = 0; i < links.Count; i++)
        {
            w.WriteStartElement("Relationship");
            w.WriteAttributeString("Id", $"rId{i + 1}");
            w.WriteAttributeString("Type", $"{RelNs}/hyperlink");
            w.WriteAttributeString("Target", links[i].Url);
            w.WriteAttributeString("TargetMode", "External");
            w.WriteEndElement();
        }
        w.WriteEndElement();
    }

    /// <summary>Excel's rules for a sheet name: ≤ 31 characters, none of []:*?/\ .</summary>
    private static string SafeSheetName(string name)
    {
        var safe = new string(XmlSafe(name).Where(c => c is not ('[' or ']' or ':' or '*' or '?' or '/' or '\\') && !char.IsControl(c)).ToArray()).Trim('\'').Trim();
        return safe.Length == 0 ? "Sheet1" : safe.Length > 31 ? safe[..31] : safe;
    }

    private static string Part(Action<XmlWriter> write)
    {
        var text = new StringBuilder();
        using (var w = XmlWriter.Create(text, new XmlWriterSettings { Encoding = Encoding.UTF8, OmitXmlDeclaration = true }))
            write(w);
        return """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>""" + "\n" + text;
    }

    private static void Add(ZipArchive zip, string path, string content)
    {
        var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
        using var stream = entry.Open();
        var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(content);
        stream.Write(bytes);
    }

    private const string ContentTypes = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/><Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/><Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/></Types>
        """;

    private const string PackageRels = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/></Relationships>
        """;

    private const string WorkbookRels = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/><Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/></Relationships>
        """;

    // cellXfs: 0 default · 1 header · 2/3 text · 4/5 text typed as text (a formula-looking value)
    // · 6/7 link · 8/9 date · 10/11 number — each second one on a striped row.
    private const string Styles = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><numFmts count="1"><numFmt numFmtId="164" formatCode="dd.mm.yyyy hh:mm"/></numFmts><fonts count="4"><font><sz val="11"/><color rgb="FF1F2937"/><name val="Calibri"/><family val="2"/></font><font><b/><sz val="11"/><color rgb="FFFFFFFF"/><name val="Calibri"/><family val="2"/></font><font><u/><sz val="11"/><color rgb="FF0563C1"/><name val="Calibri"/><family val="2"/></font><font><sz val="11"/><color rgb="FF6B7280"/><name val="Calibri"/><family val="2"/></font></fonts><fills count="4"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill><fill><patternFill patternType="solid"><fgColor rgb="FF1F4E79"/><bgColor indexed="64"/></patternFill></fill><fill><patternFill patternType="solid"><fgColor rgb="FFEEF3F9"/><bgColor indexed="64"/></patternFill></fill></fills><borders count="2"><border><left/><right/><top/><bottom/><diagonal/></border><border><left style="thin"><color rgb="FFD5DCE6"/></left><right style="thin"><color rgb="FFD5DCE6"/></right><top style="thin"><color rgb="FFD5DCE6"/></top><bottom style="thin"><color rgb="FFD5DCE6"/></bottom><diagonal/></border></borders><cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs><cellXfs count="12"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/><xf numFmtId="0" fontId="1" fillId="2" borderId="1" xfId="0" applyFont="1" applyFill="1" applyBorder="1" applyAlignment="1"><alignment horizontal="center" vertical="center" wrapText="1"/></xf><xf numFmtId="0" fontId="0" fillId="0" borderId="1" xfId="0" applyBorder="1" applyAlignment="1"><alignment vertical="center"/></xf><xf numFmtId="0" fontId="0" fillId="3" borderId="1" xfId="0" applyFill="1" applyBorder="1" applyAlignment="1"><alignment vertical="center"/></xf><xf numFmtId="0" fontId="0" fillId="0" borderId="1" xfId="0" applyBorder="1" applyAlignment="1" quotePrefix="1"><alignment vertical="center"/></xf><xf numFmtId="0" fontId="0" fillId="3" borderId="1" xfId="0" applyFill="1" applyBorder="1" applyAlignment="1" quotePrefix="1"><alignment vertical="center"/></xf><xf numFmtId="0" fontId="2" fillId="0" borderId="1" xfId="0" applyFont="1" applyBorder="1" applyAlignment="1" quotePrefix="1"><alignment vertical="center"/></xf><xf numFmtId="0" fontId="2" fillId="3" borderId="1" xfId="0" applyFont="1" applyFill="1" applyBorder="1" applyAlignment="1" quotePrefix="1"><alignment vertical="center"/></xf><xf numFmtId="164" fontId="0" fillId="0" borderId="1" xfId="0" applyNumberFormat="1" applyBorder="1" applyAlignment="1"><alignment horizontal="center" vertical="center"/></xf><xf numFmtId="164" fontId="0" fillId="3" borderId="1" xfId="0" applyNumberFormat="1" applyFill="1" applyBorder="1" applyAlignment="1"><alignment horizontal="center" vertical="center"/></xf><xf numFmtId="0" fontId="3" fillId="0" borderId="1" xfId="0" applyFont="1" applyBorder="1" applyAlignment="1"><alignment horizontal="center" vertical="center"/></xf><xf numFmtId="0" fontId="3" fillId="3" borderId="1" xfId="0" applyFont="1" applyFill="1" applyBorder="1" applyAlignment="1"><alignment horizontal="center" vertical="center"/></xf></cellXfs><cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles></styleSheet>
        """;
}
