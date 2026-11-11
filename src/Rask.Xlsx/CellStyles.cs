using System.Globalization;
using System.Xml;

namespace Rask.Workbooks;

/// <summary>
///     The styles part, read for the one thing a value needs: whether a cell's style (its <c>s</c>) shows a date.
///     A cell format (<c>cellXfs/xf</c>) names a number format, built in or one of the workbook's own (<c>numFmt</c>).
/// </summary>
internal sealed class CellStyles
{
    private readonly Dictionary<int, string> codes = [];
    private readonly List<int> formats = [];
    private DateKind[] kinds = [];

    private CellStyles()
    {
    }

    public static CellStyles None { get; } = new();

    public static CellStyles Read(XmlReader reader) => new CellStyles().Fill(reader);

    public DateKind Kind(int style) => style >= 0 && style < kinds.Length ? kinds[style] : DateKind.None;

    private CellStyles Fill(XmlReader reader)
    {
        var inCellFormats = false;
        while (reader.Read())
        {
            if (!Ooxml.IsMain(reader.NamespaceURI))
            {
                continue;
            }

            if (reader.NodeType == XmlNodeType.EndElement && reader.LocalName is "cellXfs")
            {
                inCellFormats = false;
            }
            else if (reader.NodeType == XmlNodeType.Element)
            {
                switch (reader.LocalName)
                {
                    case "cellXfs":
                        inCellFormats = !reader.IsEmptyElement;
                        break;
                    case "numFmt" when Number(reader, "numFmtId") is { } id && reader.GetAttribute("formatCode") is { } code:
                        codes[id] = code;
                        break;
                    case "xf" when inCellFormats:
                        formats.Add(Number(reader, "numFmtId") ?? 0);
                        break;
                }
            }
        }

        kinds = [.. formats.Select(id => DateFormat.Of(id, codes.GetValueOrDefault(id)))];
        return this;
    }

    private static int? Number(XmlReader reader, string attribute) =>
        int.TryParse(reader.GetAttribute(attribute), NumberStyles.None, CultureInfo.InvariantCulture, out var number) ? number : null;
}
