using System.Xml;

namespace Rask.Workbooks;

/// <summary>The workbook's shared strings (<c>sst/si</c>), which a cell of type <c>s</c> names by its place.</summary>
internal static class SharedStrings
{
    public static IReadOnlyList<string> Read(XmlReader reader, ReadLimits limits)
    {
        var strings = new List<string>();
        reader.Read();
        while (!reader.EOF)
        {
            if (reader is { NodeType: XmlNodeType.Element, LocalName: "si" } && Ooxml.IsMain(reader.NamespaceURI))
            {
                strings.Add(RichText.Read(reader));
                if (strings.Count > limits.MaxCells)
                {
                    throw Grid.TooManyCells(limits);
                }

                continue;
            }

            reader.Read();
        }

        return strings;
    }
}
