using System.Diagnostics;
using System.Globalization;
using Rask.Core;
using Rask.DevTools.Probe;

namespace Rask.DevTools.Panel;

/// <summary>
///     Takes the page's reports of how long it took to apply each frame, whichever tab is showing, and hands them to the
///     feed.
/// </summary>
/// <remarks>
///     The page measures, the panel's script collects the measurements and reports them, a few at a time, as a keydown on
///     this component's hidden element (<c>patch:12.40@2048,3.10@64</c>: milliseconds, then the frame's size, which is what
///     matches a time to its frame) — the one event both panels forward with a value. A component of its own, so a report
///     re-renders a hidden span and not the panel.
/// </remarks>
internal sealed partial class DevToolsPatchReceiver : Component
{
    /// <summary>The prefix of the keydown <c>key</c> the page's patch times arrive as.</summary>
    internal const string KeyPrefix = "patch:";

    // A report carries at most this many times; anything past it is not a real batch.
    private const int MaxPerReport = 64;

    /// <summary>The inspected session's feed.</summary>
    public required DevToolsFeed Feed { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Span.Hidden(true)
            .Data(new Dictionary<string, string?> { ["rask-devtools-patch"] = "" })
            .OnKeyDown(e => Received(e.Key));

    private void Received(string? key)
    {
        if (key is null || !key.StartsWith(KeyPrefix, StringComparison.Ordinal))
        {
            return;
        }

        var now = Stopwatch.GetTimestamp();
        var reports = key.AsSpan(KeyPrefix.Length);
        var count = 0;
        foreach (var range in reports.Split(','))
        {
            if (++count > MaxPerReport)
            {
                return;
            }

            // The page's own measurement, so anything that is not a small non-negative time and a size is not one.
            var report = reports[range];
            var at = report.IndexOf('@');
            if (at > 0
                && double.TryParse(report[..at], NumberStyles.Float, CultureInfo.InvariantCulture, out var ms)
                && ms is >= 0 and < 60_000
                && int.TryParse(report[(at + 1)..], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var bytes)
                && bytes >= -1)
            {
                Feed.PerfPatch(ms, bytes, now);
            }
        }
    }
}
