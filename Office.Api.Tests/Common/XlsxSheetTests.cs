using System.Xml.Linq;
using Office.Api.Common;

namespace Office.Api.Tests.Common;

/// <summary>
/// The .xlsx writer, read back part by part. What a spreadsheet app needs to open it the same
/// everywhere (the parts, one namespace, real cell types) and what keeps it safe (text is never a
/// formula; a stray control character can't break the file).
/// </summary>
public class XlsxSheetTests
{
    private static readonly XlsxColumn[] Columns =
    [
        new("№", XlsxColumnKind.Number, 5),
        new("Ном", XlsxColumnKind.Text, 20),
        new("Instagram", XlsxColumnKind.Link, 18),
        new("Кай", XlsxColumnKind.Date, 17),
    ];

    private static XlsxTestReader Build(params IReadOnlyList<XlsxCell>[] rows) =>
        new(XlsxSheet.Build("Контактҳо", Columns, rows, frozenColumns: 2));

    private static IReadOnlyList<XlsxCell> Person(int n, string name, string? username = null, DateTime? at = null) =>
    [
        XlsxCell.Count(n),
        XlsxCell.Of(name),
        username is null ? XlsxCell.Empty : XlsxCell.LinkTo($"@{username}", $"https://instagram.com/{username}"),
        XlsxCell.At(at),
    ];

    [Fact]
    public void HasEveryPartAnAppNeeds_AllInTheSpreadsheetNamespace()
    {
        var xlsx = Build(Person(1, "Нилуфар", "nilufar"));

        Assert.Equal(
            ["[Content_Types].xml", "_rels/.rels", "xl/_rels/workbook.xml.rels", "xl/styles.xml", "xl/workbook.xml",
             "xl/worksheets/_rels/sheet1.xml.rels", "xl/worksheets/sheet1.xml"],
            xlsx.Parts.Keys.Order(StringComparer.Ordinal));
        foreach (var part in new[] { "xl/workbook.xml", "xl/styles.xml", "xl/worksheets/sheet1.xml" })
        {
            var doc = XDocument.Parse(xlsx.Parts[part]);
            Assert.All(doc.Descendants(), e => Assert.Equal(XlsxTestReader.Main, e.Name.Namespace));
        }
        Assert.StartsWith("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>", xlsx.Parts["xl/worksheets/sheet1.xml"]);
        Assert.Contains("name=\"Контактҳо\"", xlsx.Parts["xl/workbook.xml"]);
    }

    [Fact]
    public void HeaderAndCells_HaveTheirTypesAndStyles()
    {
        var at = new DateTime(2026, 9, 25, 14, 30, 0);
        var xlsx = Build(Person(1, "Нилуфар", "nilufar", at), Person(2, "Сино"));

        Assert.Equal(["№", "Ном", "Instagram", "Кай"], xlsx.Line(0));
        Assert.All(xlsx.Rows[0], c => Assert.Equal(1, c.Style)); // the header style

        var first = xlsx.Rows[1];
        Assert.Equal(("1", 10), (first[0].Value, first[0].Style)); // a number
        Assert.Equal(("Нилуфар", "inlineStr", 2), (first[1].Text, first[1].Type, first[1].Style)); // text
        Assert.Equal(("@nilufar", 6), (first[2].Text, first[2].Style)); // a link
        Assert.Equal((at.ToOADate(), 8), (double.Parse(first[3].Value!, System.Globalization.CultureInfo.InvariantCulture), first[3].Style)); // a real date

        var second = xlsx.Rows[2]; // striped: each style + 1
        Assert.Equal([11, 3, 3, 9], second.Select(c => c.Style));
        Assert.Null(second[2].Text); // no username — an empty cell
    }

    [Fact]
    public void TextIsNeverAFormula_AndOneThatLooksLikeItIsMarkedTypedAsText()
    {
        var xlsx = Build(Person(1, "=HYPERLINK(\"http://evil.example\",\"click\")"), Person(2, "+992900000001"), Person(3, "Нилуфар"));

        Assert.Empty(xlsx.Sheet.Descendants(XlsxTestReader.Main + "f"));
        Assert.Equal("=HYPERLINK(\"http://evil.example\",\"click\")", xlsx.Rows[1][1].Text);
        Assert.Equal(4, xlsx.Rows[1][1].Style); // quotePrefix
        Assert.Equal(5, xlsx.Rows[2][1].Style); // quotePrefix, striped
        Assert.Equal(2, xlsx.Rows[3][1].Style); // plain text
        Assert.Contains("quotePrefix=\"1\"", xlsx.Parts["xl/styles.xml"]);
    }

    [Fact]
    public void Links_AreExternalRelationships_OfTheirCells()
    {
        var xlsx = Build(Person(1, "A", "nilufar"), Person(2, "B"), Person(3, "C", "sino_77"));

        var links = xlsx.Sheet.Descendants(XlsxTestReader.Main + "hyperlink")
            .Select(h => ((string)h.Attribute("ref")!, (string)h.Attribute(XlsxTestReader.Rel + "id")!)).ToList();
        Assert.Equal([("C2", "rId1"), ("C4", "rId2")], links);
        var rels = xlsx.Parts["xl/worksheets/_rels/sheet1.xml.rels"];
        Assert.Contains("Id=\"rId1\"", rels);
        Assert.Contains("Target=\"https://instagram.com/nilufar\" TargetMode=\"External\"", rels);
        Assert.Contains("Target=\"https://instagram.com/sino_77\" TargetMode=\"External\"", rels);
    }

    [Fact]
    public void HeaderAndFirstColumnsStayInView_WithAFilterOnEveryColumn()
    {
        var xlsx = Build(Person(1, "A"), Person(2, "B"));

        var pane = xlsx.Sheet.Descendants(XlsxTestReader.Main + "pane").Single();
        Assert.Equal(("2", "1", "C2", "frozen"),
            ((string)pane.Attribute("xSplit")!, (string)pane.Attribute("ySplit")!, (string)pane.Attribute("topLeftCell")!, (string)pane.Attribute("state")!));
        Assert.Equal("A1:D3", (string)xlsx.Sheet.Descendants(XlsxTestReader.Main + "autoFilter").Single().Attribute("ref")!);
        Assert.Contains("'Контактҳо'!$A$1:$D$3", xlsx.Parts["xl/workbook.xml"]);
        Assert.Equal(["5", "20", "18", "17"], xlsx.Sheet.Descendants(XlsxTestReader.Main + "col").Select(c => (string)c.Attribute("width")!));
    }

    [Fact]
    public void NoRows_StillAValidSheetWithItsHeader()
    {
        var xlsx = Build();

        Assert.Single(xlsx.Rows);
        Assert.False(xlsx.Parts.ContainsKey("xl/worksheets/_rels/sheet1.xml.rels"));
        Assert.Empty(xlsx.Sheet.Descendants(XlsxTestReader.Main + "hyperlinks"));
    }

    [Theory]
    [InlineData(0, "A")]
    [InlineData(25, "Z")]
    [InlineData(26, "AA")]
    [InlineData(51, "AZ")]
    [InlineData(701, "ZZ")]
    [InlineData(702, "AAA")]
    public void ColumnNames(int index, string name) => Assert.Equal(name, XlsxSheet.ColumnName(index));

    [Fact]
    public void XmlSafe_DropsWhatWouldBreakTheFile_KeepsEmojiAndLineBreaks()
    {
        Assert.Equal("ab", XlsxSheet.XmlSafe("a\u0000\u0001\u001Fb"));
        Assert.Equal("ab", XlsxSheet.XmlSafe("a\uD83Db")); // half an emoji
        Assert.Equal("🤍 a\tb\nc", XlsxSheet.XmlSafe("🤍 a\tb\nc"));
        Assert.Equal(32_767, XlsxSheet.XmlSafe(new string('x', 40_000)).Length);

        // And a name like that goes into the file without breaking it.
        var xlsx = Build(Person(1, "Ном\u0007\u0000 🤍"));
        Assert.Equal("Ном 🤍", xlsx.Rows[1][1].Text);
    }

    [Fact]
    public void SheetName_KeepsToExcelsRules()
    {
        var xlsx = new XlsxTestReader(XlsxSheet.Build("a/b:c*d?[e]\\" + new string('x', 40), Columns, []));

        var name = (string)XDocument.Parse(xlsx.Parts["xl/workbook.xml"]).Descendants(XlsxTestReader.Main + "sheet").Single().Attribute("name")!;
        Assert.Equal(31, name.Length);
        Assert.StartsWith("abcde", name);
    }
}
