using System.Xml;

namespace Rask.Workbooks;

/// <summary>A part's relationships (<c>_rels/x.xml.rels</c>): each id to the part it points at, and that part's type.</summary>
internal sealed class Relationships
{
    private readonly Dictionary<string, (string Type, string Part)> byId = new(StringComparer.Ordinal);

    private Relationships()
    {
    }

    /// <summary>The relationships of a part (<c>""</c> for the package itself); none when it has no <c>.rels</c>.</summary>
    public static Relationships Of(Package package, string part)
    {
        var slash = part.LastIndexOf('/');
        var rels = $"{part[..(slash + 1)]}_rels/{part[(slash + 1)..]}.rels";
        var relationships = new Relationships();
        return package.Has(rels) ? package.Read(rels, reader => relationships.Fill(reader, part)) : relationships;
    }

    /// <summary>Whether a type URI is of the type named by its last segment (<c>worksheet</c>, <c>styles</c>).</summary>
    public static bool IsType(string? uri, string type) => uri is not null && uri.EndsWith("/" + type, StringComparison.Ordinal);

    public (string Type, string Part)? Find(string id) => byId.TryGetValue(id, out var target) ? target : null;

    /// <summary>The first part of a type, named by the last segment of its type URI.</summary>
    public string? First(string type) =>
        byId.Values.Where(target => IsType(target.Type, type)).Select(target => target.Part).FirstOrDefault();

    private Relationships Fill(XmlReader reader, string from)
    {
        while (reader.Read())
        {
            if (reader is { NodeType: XmlNodeType.Element, LocalName: "Relationship" }
                && reader.GetAttribute("TargetMode") is not "External"
                && reader.GetAttribute("Id") is { } id
                && reader.GetAttribute("Target") is { } target)
            {
                byId.TryAdd(id, (reader.GetAttribute("Type") ?? "", Package.Resolve(from, target)));
            }
        }

        return this;
    }
}
