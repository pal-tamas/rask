using System.Buffers;
using System.Globalization;
using System.Text;

namespace Rask.Workbooks;

/// <summary>Text for an XML element or attribute. XML 1.0 cannot hold most control characters at all; OOXML writes them as <c>_x0001_</c>.</summary>
internal static class XmlText
{
    private static readonly SearchValues<char> HexDigits = SearchValues.Create("0123456789abcdefABCDEF");

    public static string Escape(string text)
    {
        if (!NeedsEscape(text))
        {
            return text;
        }

        var escaped = new StringBuilder(text.Length + 16);
        var at = 0;
        while (at < text.Length)
        {
            var c = text[at++];
            switch (c)
            {
                case '&': escaped.Append("&amp;"); break;
                case '<': escaped.Append("&lt;"); break;
                case '>': escaped.Append("&gt;"); break;
                case '"': escaped.Append("&quot;"); break;
                case '\'': escaped.Append("&apos;"); break;
                case '_' when LooksLikeEscape(text, at - 1): escaped.Append("_x005F_"); break;
                case var _ when char.IsHighSurrogate(c) && at < text.Length && char.IsLowSurrogate(text[at]):
                    escaped.Append(c).Append(text[at++]);
                    break;
                case var _ when !IsXmlChar(c): escaped.Append(CultureInfo.InvariantCulture, $"_x{(int)c:X4}_"); break;
                default: escaped.Append(c); break;
            }
        }

        return escaped.ToString();
    }

    private static bool NeedsEscape(string text)
    {
        for (var at = 0; at < text.Length; at++)
        {
            var c = text[at];
            if (c is '&' or '<' or '>' or '"' or '\'' or '_' || !IsXmlChar(c))
            {
                return true;
            }
        }

        return false;
    }

    // XML 1.0 Char, less the surrogates (a pair is let through above, a lone one is not text).
    private static bool IsXmlChar(char c) => c is '\t' or '\n' or '\r' || (c >= ' ' && !char.IsSurrogate(c) && c is not ('￾' or '￿'));

    // "_x0041_" in a user's text would be read back as "A"; its underscore is written as _x005F_.
    private static bool LooksLikeEscape(string text, int at) =>
        at + 6 < text.Length
        && text[at + 1] is 'x' or 'X'
        && text[at + 6] == '_'
        && !text.AsSpan(at + 2, 4).ContainsAnyExcept(HexDigits);
}
