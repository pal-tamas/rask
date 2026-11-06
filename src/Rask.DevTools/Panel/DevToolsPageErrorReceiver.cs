using System.Text.Json;
using Rask.Core;
using Rask.DevTools.Probe;

namespace Rask.DevTools.Panel;

/// <summary>
///     Takes the failures the page reports about itself — a script's uncaught error, a rejected promise — and lists them
///     with the page's errors, whichever tab is showing.
/// </summary>
/// <remarks>
///     No server hears of these: the page's devtools host catches them and posts them to the panel, whose script hands
///     each one over as a keydown on this component's hidden element, the report's JSON after <see cref="KeyPrefix" />.
///     Everything in it is the page's say-so, so every field is bounded and anything malformed is dropped.
/// </remarks>
internal sealed partial class DevToolsPageErrorReceiver : Component
{
    /// <summary>The prefix of the keydown <c>key</c> a page failure arrives as.</summary>
    internal const string KeyPrefix = "page-error:";

    /// <summary>The prefix of the keydown <c>key</c> the page's browser is named in, for a bug report.</summary>
    internal const string BrowserKeyPrefix = "browser:";

    private const int TitleLimit = 200;
    private const int MessageLimit = 2000;

    /// <summary>The inspected session's feed.</summary>
    public required DevToolsFeed Feed { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Span.Hidden(true)
            .Data("rask-devtools-page-errors", "")
            .OnKeyDown(e => Received(e.Key));

    private void Received(string? key)
    {
        if (key is not null && key.StartsWith(BrowserKeyPrefix, StringComparison.Ordinal))
        {
            var browser = key[BrowserKeyPrefix.Length..];
            Feed.Browser = browser.Length <= 80 ? browser : browser[..80];
            return;
        }

        if (key is null || !key.StartsWith(KeyPrefix, StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            using var json = JsonDocument.Parse(key.AsMemory(KeyPrefix.Length));
            Record(Feed, json.RootElement);
        }
        catch (JsonException)
        {
            // Not a report: nothing to list.
        }
    }

    /// <summary>Lists one report, when it is one.</summary>
    internal static void Record(DevToolsFeed feed, JsonElement report)
    {
        if (report.ValueKind != JsonValueKind.Object
            || Text(report, "title", TitleLimit) is not { } title
            || Text(report, "message", MessageLimit) is not { } message)
        {
            return;
        }

        var at = report.TryGetProperty("at", out var ms) && ms.TryGetInt64(out var millis)
                 && millis is > 0 and < 253402300800000
            ? DateTimeOffset.FromUnixTimeMilliseconds(millis).ToLocalTime()
            : TimeProvider.System.GetLocalNow();

        var stack = Text(report, "stack", DevToolsErrorLog.DetailLimit);
        feed.Errors.Record(
            DevToolsErrorKind.Page, isWarning: false, title, message, stack,
            null, null, caught: false, appWide: false, at, DevToolsBugReport.FromScript(stack));
    }

    private static string? Text(JsonElement report, string name, int limit)
    {
        if (!report.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String
            || value.GetString() is not { } text)
        {
            return null;
        }

        return text.Length <= limit ? text : text[..limit];
    }
}
