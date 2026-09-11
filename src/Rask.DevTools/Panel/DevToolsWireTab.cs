using System.Diagnostics;
using System.Globalization;
using Rask.Core;
using Rask.DevTools.Probe;
using Rask.Ui;

namespace Rask.DevTools.Panel;

/// <summary>
///     The Wire tab: every frame the inspected page and the app exchanged, newest first, with what each one cost.
/// </summary>
/// <remarks>
///     <para>
///         Directions are the page's. <em>Sent</em> is an event, a navigation or a hello going to the app;
///         <em>received</em> is what the app sent back. A render frame shows how many edit ops its diff carried, and
///         nothing when the app sent the whole document instead.
///     </para>
///     <para>
///         The tab follows the feed while it is mounted and lets go of it when it leaves. Refreshes go through
///         <see cref="DevToolsRefreshGate" />, so a burst of traffic costs the panel one render per interval.
///     </para>
/// </remarks>
internal sealed partial class DevToolsWireTab : Component
{
    /// <summary>How many of the newest frames the table lists. The feed holds more, and the totals count all of them.</summary>
    internal const int RowLimit = 200;

    private DevToolsFeed? _following;
    private DevToolsRefreshGate? _gate;

    /// <summary>The inspected session's feed.</summary>
    public required DevToolsFeed Feed { get; set; }

    /// <inheritdoc />
    protected override void OnMount()
    {
        // The lifetime token, read in a lifecycle hook: it is cancelled when the tab leaves the tree, so a refresh that
        // is already scheduled by then never runs.
        _gate = new DevToolsRefreshGate(StateHasChanged, CancellationToken);
        _following = Feed;
        _following.Changed += OnFeedChanged;
    }

    /// <inheritdoc />
    protected override void OnUnmount()
    {
        // The feed belongs to the inspected session and outlives the panel. A handler left on it would keep this
        // component, and the panel session it renders in, alive for as long as the inspected page stays open.
        if (_following is { } feed)
        {
            feed.Changed -= OnFeedChanged;
            _following = null;
        }
    }

    /// <inheritdoc />
    protected override Component? Render()
    {
        var events = Feed.WireSnapshot();
        if (events.Length == 0)
        {
            return UiAlert["No traffic yet. Use the page, and every frame it exchanges with the app is listed here."];
        }

        int sent = 0, received = 0;
        long bytesSent = 0, bytesReceived = 0;
        foreach (var e in events)
        {
            if (e.Direction == DevToolsWireDirection.Out)
            {
                sent++;
                bytesSent += e.Bytes;
            }
            else
            {
                received++;
                bytesReceived += e.Bytes;
            }
        }

        var shown = Math.Min(events.Length, RowLimit);
        var rows = new Component[shown];
        for (var i = 0; i < shown; i++)
        {
            var at = events.Length - 1 - i;
            rows[i] = Row(events[at], at > 0 ? events[at - 1] : null);
        }

        return Div.Class("flex flex-col gap-3")[
            UiMetricRow[
                UiMetric.Label("Frames sent").Value(Count(sent)),
                UiMetric.Label("Frames received").Value(Count(received)),
                UiMetric.Label("Bytes sent").Value(Size(bytesSent)),
                UiMetric.Label("Bytes received").Value(Size(bytesReceived))
            ],
            P.Class("text-xs opacity-60")[
                shown == events.Length
                    ? $"{Count(events.Length)} frames, newest first."
                    : $"The newest {Count(shown)} of {Count(events.Length)} frames."
            ],
            UiTable.Scroll(true)[
                Thead[Tr[Th["#"], Th["Direction"], Th["Type"], Th["Size"], Th["Diff"], Th["Gap"]]],
                Tbody[rows]
            ]
        ];
    }

    private void OnFeedChanged() => _gate?.Notify();

    private static Component Row(DevToolsWireEvent e, DevToolsWireEvent? previous) =>
        Tr.Key(e.Sequence)[
            Td.Class("tabular-nums opacity-60")[e.Sequence.ToString(CultureInfo.InvariantCulture)],
            Td[e.Direction == DevToolsWireDirection.Out ? UiBadge.Tone(UiTone.Info)["sent"] : UiBadge["received"]],
            Td.Class("font-mono")[e.Kind],
            Td.Class("tabular-nums whitespace-nowrap")[Size(e.Bytes)],
            Td.Class("tabular-nums whitespace-nowrap")[e.DiffOps is { } ops ? Count(ops) + (ops == 1 ? " op" : " ops") : ""],
            // Time since the frame before it, which is what a round trip or a chatty handler looks like in a list.
            Td.Class("tabular-nums whitespace-nowrap opacity-60")[previous is { } p ? Gap(p.Timestamp, e.Timestamp) : ""]
        ];

    private static string Count(long value) => value.ToString("N0", CultureInfo.InvariantCulture);

    private static string Size(long bytes) => bytes switch
    {
        < 1024 => bytes.ToString(CultureInfo.InvariantCulture) + " B",
        < 1024 * 1024 => (bytes / 1024d).ToString("0.0", CultureInfo.InvariantCulture) + " KB",
        _ => (bytes / (1024d * 1024d)).ToString("0.0", CultureInfo.InvariantCulture) + " MB",
    };

    private static string Gap(long from, long to)
    {
        // No leading "+": the column header says what the number is, and the HTML encoder writes a plus sign as six bytes
        // on every row of every refresh.
        var milliseconds = Stopwatch.GetElapsedTime(from, to).TotalMilliseconds;
        return milliseconds.ToString(milliseconds < 10 ? "0.0" : "0", CultureInfo.InvariantCulture) + " ms";
    }
}
