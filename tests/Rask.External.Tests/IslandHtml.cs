using System.Text;
using System.Text.Json;
using Rask.Core.Live;

namespace Rask.External.Tests;

#pragma warning disable RASK014 // the tests hand the very instance they render to the renderer

/// <summary>Renders islands and reads back what the browser would receive.</summary>
internal static class IslandHtml
{
    /// <summary>The markup a component serializes to, outside any live session.</summary>
    public static string Render(Component component)
    {
        var sb = new StringBuilder();
        HtmlSerializer.Serialize(component, sb);
        return sb.ToString();
    }

    /// <summary>The first props attribute's decoded JSON.</summary>
    public static string ReadProps(string html)
    {
        const string marker = " props=\"";
        var start = html.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"no props attribute in: {html}");

        start += marker.Length;
        var end = html.IndexOf('"', start);
        Assert.True(end > start, $"unterminated props attribute in: {html}");

        // The serializer HTML-encodes the attribute value, so the quotes inside the JSON arrive as
        // &quot;. Decoding here rather than asserting on the encoded form keeps the tests about the
        // props rather than about HTML escaping.
        return System.Net.WebUtility.HtmlDecode(html[start..end]);
    }

    /// <summary>The props of <paramref name="component" />, parsed.</summary>
    public static JsonDocument Props(Component component) => JsonDocument.Parse(ReadProps(Render(component)));

    /// <summary>
    ///     Renders <paramref name="root" /> through a live context — which is what mints handler ids a
    ///     dispatch can resolve — and returns the markup.
    /// </summary>
    public static string RenderLive(Component root)
    {
        using var cache = new SessionRenderCache();
        using (FrameSinkScope.Push(cache.PrepareCurrentBuffer()))
        {
            return root.RenderAsLiveRoot();
        }
    }
}
