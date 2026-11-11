using System.Xml;

namespace Rask.Workbooks;

/// <summary>
///     What the workbook part says about the rest: its sheets of cells in tab order, which date system its serials
///     count in, and where its styles and shared strings are.
/// </summary>
internal sealed class Workbook
{
    private const string DefaultPart = "xl/workbook.xml";

    private readonly List<(string Name, string Part)> sheets = [];

    private Workbook()
    {
    }

    /// <summary>Whether day 0 is 1 January 1904 (an old Mac workbook) rather than Excel's 1900.</summary>
    public bool Date1904 { get; private set; }

    public string? Styles { get; private set; }

    public string? SharedStrings { get; private set; }

    public static Workbook Open(Package package)
    {
        var part = Relationships.Of(package, "").First("officeDocument") ?? DefaultPart;
        var relationships = Relationships.Of(package, part);
        var workbook = package.Read(part, reader => new Workbook().Fill(reader, relationships));
        workbook.Styles = relationships.First("styles");
        workbook.SharedStrings = relationships.First("sharedStrings");
        return workbook.sheets.Count > 0 ? workbook : throw Package.Malformed("it has no sheet of cells");
    }

    /// <summary>The first sheet, or the one of that name - compared as Excel compares names, without regard to case.</summary>
    public (string Name, string Part) Sheet(string? sheet)
    {
        if (sheet is null)
        {
            return sheets[0];
        }

        var named = sheets.FindIndex(candidate => string.Equals(candidate.Name, sheet, StringComparison.OrdinalIgnoreCase));
        return named >= 0
            ? sheets[named]
            : throw new ArgumentException(
                $"The workbook has no sheet named '{sheet}'. Its sheets are: {string.Join(", ", sheets.Select(candidate => $"'{candidate.Name}'"))}.",
                nameof(sheet));
    }

    private Workbook Fill(XmlReader reader, Relationships relationships)
    {
        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element || !Ooxml.IsMain(reader.NamespaceURI))
            {
                continue;
            }

            if (reader.LocalName is "workbookPr")
            {
                Date1904 = reader.GetAttribute("date1904") is "1" or "true";
            }
            else if (reader.LocalName is "sheet" && reader.GetAttribute("name") is { } name
                && RelationshipId(reader) is { } id
                && relationships.Find(id) is { } target
                && Relationships.IsType(target.Type, "worksheet"))
            {
                // a chart sheet has no cells to read
                sheets.Add((name, target.Part));
            }
        }

        return this;
    }

    private static string? RelationshipId(XmlReader reader)
    {
        for (var at = 0; at < reader.AttributeCount; at++)
        {
            reader.MoveToAttribute(at);
            if (reader.LocalName is "id" && Ooxml.IsOffice(reader.NamespaceURI))
            {
                var id = reader.Value;
                reader.MoveToElement();
                return id;
            }
        }

        reader.MoveToElement();
        return null;
    }
}
