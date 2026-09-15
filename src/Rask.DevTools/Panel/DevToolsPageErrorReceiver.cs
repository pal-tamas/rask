using System.Text.Json;
using Rask.Core;
using Rask.DevTools.Probe;

namespace Rask.DevTools.Panel;

/// <summary>
///     Takes the failures the page reports about itself — a script's uncaught error, a rejected promise, an island that
///     failed — and lists them with the page's errors, whichever tab is showing.
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
            .Data(new Dictionary<string, string?> { ["rask-devtools-page-errors"] = "" })
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

        var island = Text(report, "island", TitleLimit);
        var kind = Text(report, "kind", 16) == "island" ? DevToolsErrorKind.Island : DevToolsErrorKind.Page;
        var at = report.TryGetProperty("at", out var ms) && ms.TryGetInt64(out var millis)
                 && millis is > 0 and < 253402300800000
            ? DateTimeOffset.FromUnixTimeMilliseconds(millis).ToLocalTime()
            : DateTimeOffset.Now;

        if (kind == DevToolsErrorKind.Island)
        {
            message = Text(report, "phase", 16) switch
            {
                "props" => $"'{island}' has props it could not read: {message}",
                "update" => $"'{island}' failed to update: {message}",
                "unmount" => $"'{island}' failed to unmount: {message}",
                _ => $"'{island}' failed to mount: {message}",
            };
        }

        // An island is inside a component the page rendered: found by where its element is, as a pick would find it.
        (DevToolsComponentNode Component, List<string> Path)? owner = null;
        if (Text(report, "place", 512) is { } place && DevToolsPlaceMatch.Parse(place) is { } parsed
            && feed.TreeSnapshot() is { } tree)
        {
            owner = DevToolsPlaceMatch.Find(tree, [.. parsed.Path, parsed.First]);
        }

        var stack = Text(report, "stack", DevToolsErrorLog.DetailLimit);
        var entry = feed.Errors.Record(
            kind, isWarning: false, title, message, stack,
            owner?.Component.Type, owner?.Component.Id, caught: false, appWide: false, at, DevToolsBugReport.FromScript(stack));
        if (owner is { Path: var path })
        {
            for (var i = path.Count - 2; i >= 0; i--)
            {
                feed.Errors.Enclosing(entry, path[i]);
            }
        }
    }

    private static string? Text(JsonElement report, string name, int limit) =>
        report.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && value.GetString() is { } text
            ? text.Length <= limit ? text : text[..limit]
            : null;
}
