using System.IO.Compression;
using System.Xml.Linq;

namespace Office.Api.Tests.Common;

/// <summary>Reads back what XlsxSheet wrote — the parts, and every cell of the one sheet.</summary>
public sealed class XlsxTestReader
{
    public static readonly XNamespace Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    public static readonly XNamespace Rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    public record Cell(string Ref, string? Text, string? Value, int Style, string? Type);

    public Dictionary<string, string> Parts { get; } = [];
    public XDocument Sheet { get; }
    public List<List<Cell>> Rows { get; }

    public XlsxTestReader(byte[] file)
    {
        using var zip = new ZipArchive(new MemoryStream(file), ZipArchiveMode.Read);
        foreach (var entry in zip.Entries)
        {
            using var reader = new StreamReader(entry.Open());
            Parts[entry.FullName] = reader.ReadToEnd();
        }

        Sheet = XDocument.Parse(Parts["xl/worksheets/sheet1.xml"]);
        Rows = Sheet.Descendants(Main + "row")
            .Select(row => row.Elements(Main + "c").Select(c => new Cell(
                (string)c.Attribute("r")!,
                c.Element(Main + "is")?.Element(Main + "t")?.Value,
                c.Element(Main + "v")?.Value,
                (int?)c.Attribute("s") ?? 0,
                (string?)c.Attribute("t"))).ToList())
            .ToList();
    }

    /// <summary>What a person sees in the cell: the text, or the raw number/date value.</summary>
    public List<string> Line(int index) => Rows[index].Select(c => c.Text ?? c.Value ?? "").ToList();
}
