using System.Text.Json;
using Xunit;

namespace Rask.TestSupport;

/// <summary>
///     Helpers for pulling values out of rendered Rask HTML / live payloads in tests.
///     Consolidates the per-file <c>ExtractAttr</c> / <c>ExtractSessionId</c> /
///     <c>ExtractFirstHandlerId</c> copies that had drifted between projects.
///     Named <c>Markup</c> (not <c>Html</c>) to avoid clashing with the
///     <see cref="Rask.Core.Components.Html" /> tag component.
/// </summary>
public static class Markup
{
    /// <summary>
    ///     Returns the value of <paramref name="name" />=&quot;…&quot; in
    ///     <paramref name="html" />, or <c>null</c> if the attribute is absent.
    /// </summary>
    public static string? Attr(string html, string name)
    {
        var marker = name + "=\"";
        var from = 0;
        while (true)
        {
            var i = html.IndexOf(marker, from, StringComparison.Ordinal);
            if (i < 0)
            {
                return null;
            }

            // Require an attribute boundary before the name (start / ASCII whitespace) so a short name
            // (e.g. "label") doesn't match inside a longer one ("aria-label").
            var before = i == 0 ? ' ' : html[i - 1];
            if (before is ' ' or '\t' or '\n' or '\r' or '\f')
            {
                var start = i + marker.Length;
                var end = html.IndexOf('"', start);
                return end < 0 ? null : html.Substring(start, end - start);
            }

            from = i + marker.Length;
        }
    }

    /// <summary>
    ///     Returns the value of <paramref name="name" />=&quot;…&quot;, asserting that the
    ///     attribute is present (fails the test otherwise).
    /// </summary>
    public static string RequireAttr(string html, string name)
    {
        var value = Attr(html, name);
        Assert.True(value is not null, $"no {name} attribute found in html: {html}");
        return value!;
    }

    /// <summary>Returns the <c>data-rask-root</c> session id from a rendered shell.</summary>
    public static string SessionId(string html) => RequireAttr(html, "data-rask-root");

    /// <summary>The first <c>data-rask-on-click</c> handler id in rendered HTML (asserts present).</summary>
    public static string FirstHandlerId(string html) => RequireAttr(html, "data-rask-on-click");

    /// <summary>
    ///     Parses the <c>html</c> field out of a JSON live payload and returns the first
    ///     <c>data-rask-on-click</c> handler id, asserting one is present.
    /// </summary>
    public static string FirstHandlerId(byte[] jsonPayload)
    {
        using var doc = JsonDocument.Parse(jsonPayload.AsMemory());
        return FirstHandlerId(doc.RootElement.GetProperty("html").GetString()!);
    }
}
