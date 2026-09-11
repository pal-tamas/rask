using System.Globalization;
using System.Text;

namespace Rask.External.Tasks;

/// <summary>JSON text written and read by hand, for the two small documents this assembly exchanges with Node.</summary>
/// <remarks>
///     By hand because this assembly is loaded into the MSBuild host, where bringing a serializer along to write a
///     handful of strings is a bigger risk than the few lines here — the same call the TypeScript tool resolver makes.
/// </remarks>
internal static class JsonText
{
    /// <summary><paramref name="value" /> as a quoted JSON string, or <c>null</c>.</summary>
    public static string String(string? value)
    {
        if (value is null)
        {
            return "null";
        }

        var sb = new StringBuilder(value.Length + 2);
        sb.Append('"');
        foreach (var c in value)
        {
            switch (c)
            {
                case '"':
                    sb.Append("\\\"");
                    break;
                case '\\':
                    sb.Append("\\\\");
                    break;
                case '\n':
                    sb.Append("\\n");
                    break;
                case '\r':
                    sb.Append("\\r");
                    break;
                case '\t':
                    sb.Append("\\t");
                    break;
                default:
                    // U+2028 and U+2029 are valid inside a JSON string but end a line in older JavaScript
                    // parsers, so they are escaped along with the control characters.
                    if (c < 0x20 || c == '\u2028' || c == '\u2029')
                    {
                        sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        sb.Append(c);
                    }

                    break;
            }
        }

        sb.Append('"');
        return sb.ToString();
    }

    /// <summary>Reverses JSON string escaping for the contents between the quotes.</summary>
    public static string Unescape(string raw)
    {
        if (raw.IndexOf('\\') < 0)
        {
            return raw;
        }

        var sb = new StringBuilder(raw.Length);
        for (var i = 0; i < raw.Length; i++)
        {
            if (raw[i] != '\\' || i + 1 >= raw.Length)
            {
                sb.Append(raw[i]);
                continue;
            }

            var e = raw[++i];
            switch (e)
            {
                case 'n':
                    sb.Append('\n');
                    break;
                case 'r':
                    sb.Append('\r');
                    break;
                case 't':
                    sb.Append('\t');
                    break;
                case 'b':
                    sb.Append('\b');
                    break;
                case 'f':
                    sb.Append('\f');
                    break;
                case 'u' when i + 4 < raw.Length
                              && int.TryParse(raw.Substring(i + 1, 4), NumberStyles.HexNumber,
                                  CultureInfo.InvariantCulture, out var code):
                    sb.Append((char)code);
                    i += 4;
                    break;
                default:
                    sb.Append(e);
                    break;
            }
        }

        return sb.ToString();
    }
}
