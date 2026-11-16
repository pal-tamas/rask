using System.Text.Json;

namespace Rask.Server.Tests.Infrastructure;

/// <summary>What a test reads off a page or a frame: the session a page was served with, a frame's string field.</summary>
internal static class FrameFields
{
    /// <summary>The session id the page's <c>data-rask-root</c> names.</summary>
    public static string SessionIdFrom(string html)
    {
        const string marker = "data-rask-root=\"";
        var start = html.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, "the GET shell must carry data-rask-root");
        start += marker.Length;
        return html[start..html.IndexOf('"', start)];
    }

    /// <summary>A top-level string of a frame, or null when the frame has none by that name.</summary>
    public static string? StringField(string frame, string name)
    {
        using var doc = JsonDocument.Parse(frame);
        return doc.RootElement.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }
}
