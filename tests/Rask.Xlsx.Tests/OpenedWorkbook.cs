using System.IO.Compression;
using System.Xml.Linq;

namespace Rask.Workbooks.Tests;

/// <summary>A workbook read back the way Excel reads it: every part must be well-formed XML.</summary>
internal sealed class OpenedWorkbook
{
    public static readonly XNamespace Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    private readonly Dictionary<string, XDocument> parts = new(StringComparer.Ordinal);

    public OpenedWorkbook(byte[] file)
    {
        using var zip = new ZipArchive(new MemoryStream(file), ZipArchiveMode.Read);
        foreach (var entry in zip.Entries)
        {
            using var stream = entry.Open();
            parts[entry.FullName] = XDocument.Load(stream);
        }
    }

    public IEnumerable<string> PartNames => parts.Keys;

    public XDocument Part(string name) => parts[name];

    public XElement Sheet(int number) => parts[$"xl/worksheets/sheet{number}.xml"].Root!;

    public IEnumerable<XElement> Rows(int sheet) => Sheet(sheet).Descendants(Main + "row");

    public XElement Cell(int sheet, string reference) =>
        Sheet(sheet).Descendants(Main + "c").Single(c => (string?)c.Attribute("r") == reference);

    public string Text(int sheet, string reference) => Cell(sheet, reference).Descendants(Main + "t").Single().Value;
}
