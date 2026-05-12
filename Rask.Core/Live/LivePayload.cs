using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using Rask.Core.Authentication;
using Rask.Core.ScopedCss;

namespace Rask.Core.Live;

public static class LivePayload
{
    private static readonly Regex BodyOpen = new(@"<body\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex BodyElement = new(@"<body\b[^>]*>.*?</body>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    public static string InjectRootAttr(string html, string sessionId)
    {
        var encoded = HtmlEncoder.Default.Encode(sessionId);
        return BodyOpen.Replace(html, $"<body data-rask-root=\"{encoded}\"", 1);
    }

    public static string ExtractBody(string html)
    {
        var match = BodyElement.Match(html);
        return match.Success ? match.Value : html;
    }

    public static string BuildPayload(
        string html,
        string? historyUrl,
        bool replace,
        string? cssText = null,
        AuthInstruction? auth = null)
    {
        var cssHash = ScopedCssRegistry.CurrentHash;

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("html", html);
            writer.WriteString("cssHash", cssHash);

            if (cssText is not null)
            {
                writer.WriteString("cssText", cssText);
            }

            if (historyUrl is not null)
            {
                writer.WriteStartObject("history");
                writer.WriteString("action", replace ? "replace" : "push");
                writer.WriteString("url", historyUrl);
                writer.WriteEndObject();
            }

            if (auth is not null)
            {
                writer.WriteStartObject("auth");
                writer.WriteString("ticket", auth.Ticket);
                if (auth.ReturnUrl is not null)
                {
                    writer.WriteString("returnUrl", auth.ReturnUrl);
                }

                writer.WriteEndObject();
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
