using System.Text;
using System.Xml;

namespace Rask.Workbooks;

/// <summary>
///     A string as a workbook holds it (a shared <c>si</c> or an inline <c>is</c>): a plain <c>t</c>, or runs of rich
///     text (<c>r/t</c>) joined - never the phonetic guide (<c>rPh</c>) printed above East Asian text.
/// </summary>
internal static class RichText
{
    /// <summary>Reads the string at the reader's element and leaves the reader on the node after it.</summary>
    public static string Read(XmlReader reader)
    {
        if (reader.IsEmptyElement)
        {
            reader.Read();
            return "";
        }

        var text = new StringBuilder();
        var depth = reader.Depth;
        string? child = null;
        reader.Read();
        while (!reader.EOF && reader.Depth > depth)
        {
            if (reader.NodeType == XmlNodeType.Element)
            {
                var below = reader.Depth - depth;
                if (below == 1)
                {
                    child = reader.LocalName;
                }

                if (reader.LocalName is "t" && (below == 1 || (below == 2 && child is "r")))
                {
                    text.Append(reader.ReadElementContentAsString());
                    continue;
                }
            }

            reader.Read();
        }

        reader.Read();
        return XmlText.Unescape(text.ToString());
    }
}
