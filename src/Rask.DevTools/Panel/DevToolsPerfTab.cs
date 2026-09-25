using System.Diagnostics;
using System.Globalization;
using Rask.Core;
using Rask.DevTools.Probe;

namespace Rask.DevTools.Panel;

/// <summary>
///     The Perf tab: where each interaction's time went — the handler, the render, the diff, the bytes on the wire and the
///     page applying them — and which components take the longest to render.
/// </summary>
/// <remarks>
///     <para>
///         An interaction starts with a page event whose handler ran, or a navigation, and ends with the frame it sent. A
///         render nothing on the page asked for — a timer, a push — is an interaction of its own, named <c>render</c>.
///     </para>
///     <para>
///         Patch time is the page's own measurement, reported to the panel after each frame is applied. The page and the app
///         keep different clocks, so each report is matched by the frame's size to the oldest frame of that size still
///         waiting for one (see <see cref="DevToolsFeed.PatchWindow" />).
///     </para>
/// </remarks>
internal sealed partial class DevToolsPerfTab : Component
{
    /// <summary>How many of the newest interactions the table lists.</summary>
    internal const int RowLimit = 100;

    /// <summary>How many components the slowest-components table lists.</summary>
    internal const int SlowestLimit = 20;

    private DevToolsFeed? _following;
    private DevToolsRefreshGate? _gate;

    /// <summary>The inspected session's feed.</summary>
    public required DevToolsFeed Feed { get; set; }

    // Reads the feed, which the render cache cannot see.
    /// <inheritdoc />
    protected override bool BypassRenderCache => true;

    /// <inheritdoc />
    protected override Task OnMount()
    {
        _gate = new DevToolsRefreshGate(StateHasChanged, CancellationToken);
        _following = Feed;
        _following.Changed += OnFeedChanged;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    protected override Task OnUnmount()
    {
        // The feed outlives the panel; a handler left on it would keep this tab and its session alive.
        if (_following is { } feed)
        {
            feed.Changed -= OnFeedChanged;
            _following = null;
        }
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    protected override Component? Render()
    {
        var interactions = Feed.InteractionsSnapshot();
        var slowest = Slowest(Feed.CommitsSnapshot());

        return Div.Class("flex flex-col gap-3")[
            Div.Class("flex flex-wrap items-center justify-between gap-2")[
                P.Class("text-xs opacity-60")[
                    "Server is the handler, the render and the diff; patch is the page applying what arrived."
                ],
                Ui.Button.Size(Ui.Size.Sm).Title("Forget the interactions timed so far").OnClick(Feed.ClearInteractions)["Clear"]
            ],
            interactions.Length == 0
                ? Ui.Alert["No interactions yet. Use the page, and each event's handler, render, diff and patch are timed here."]
                : Div.Class("flex flex-col gap-3")[
                    Ui.MetricRow[
                        Ui.Metric.Label("Interactions").Value(Count(interactions.Length)),
                        Ui.Metric.Label("Server p50").Value(Milliseconds(Percentile(interactions, 0.50))),
                        Ui.Metric.Label("Server p95").Value(Milliseconds(Percentile(interactions, 0.95))),
                        Ui.Metric.Label("Patch p50").Value(PatchPercentile(interactions, 0.50))
                    ],
                    InteractionTable(interactions)
                ],
            slowest.Count == 0
                ? null
                : Div.Class("flex flex-col gap-3")[
                    P.Class("text-sm font-semibold")["Slowest components"],
                    SlowestTable(slowest)
                ]
        ];
    }

    private void OnFeedChanged() => _gate?.Notify();

    private static Component InteractionTable(DevToolsInteraction[] interactions)
    {
        var shown = Math.Min(interactions.Length, RowLimit);
        var rows = new Component[shown];
        for (var i = 0; i < shown; i++)
        {
            var item = interactions[interactions.Length - 1 - i];
            rows[i] = Tr.Key(item.Sequence)[
                Td.Class("tabular-nums opacity-60")[item.Sequence.ToString(CultureInfo.InvariantCulture)],
                Td[Trigger(item)],
                Td.Class("tabular-nums whitespace-nowrap")[item.HandlerTicks is { } handler ? Milliseconds(handler) : ""],
                Td.Class("tabular-nums whitespace-nowrap")[item.RenderTicks > 0 ? Milliseconds(item.RenderTicks) : ""],
                Td.Class("tabular-nums whitespace-nowrap")[item.DiffTicks > 0 ? Milliseconds(item.DiffTicks) : ""],
                Td.Class("tabular-nums whitespace-nowrap")[item.Frames == 0 ? "nothing sent" : Size(item.Bytes)],
                Td.Class("tabular-nums whitespace-nowrap")[
                    item.PatchMilliseconds is { } patch ? Milliseconds(patch) : item.Frames == 0 ? "" : "…"
                ],
                Td.Class("tabular-nums whitespace-nowrap font-semibold")[
                    Milliseconds(Stopwatch.GetElapsedTime(0, item.ServerTicks).TotalMilliseconds + (item.PatchMilliseconds ?? 0))
                ]
            ];
        }

        return Div.Class("flex flex-col gap-3")[
            P.Class("text-xs opacity-60")[
                shown == interactions.Length
                    ? "Newest first."
                    : $"The newest {Count(shown)} of {Count(interactions.Length)} interactions."
            ],
            Ui.Table.Scroll(true)[
                Thead[Tr[Th["#"], Th["Trigger"], Th["Handler"], Th["Render"], Th["Diff"], Th["Size"], Th["Patch"], Th["Total"]]],
                Tbody[rows]
            ]
        ];
    }

    private static Component Trigger(DevToolsInteraction item) =>
        Span.Class("flex flex-wrap items-center gap-1")[
            Ui.Badge.Size(Ui.Size.Sm).Mono(true).Tone(item.Faulted ? Ui.Tone.Error : null)[item.Trigger],
            item.Target is { } target ? Span.Class("font-mono")[target] : null,
            item.Faulted ? Span.Class("text-xs")["threw"] : null
        ];

    /// <summary>One component instance's render times across the commits held.</summary>
    internal sealed record SlowComponent(long Id, string Type, string? Key, int Renders, long Ticks, long MaxTicks);

    /// <summary>Components by their total own render time, slowest first.</summary>
    internal static List<SlowComponent> Slowest(DevToolsCommit[] commits)
    {
        var byId = new Dictionary<long, SlowComponent>();
        foreach (var commit in commits)
        {
            foreach (var render in commit.Renders)
            {
                if (render.SelfTicks <= 0)
                {
                    continue;
                }

                byId[render.Id] = byId.TryGetValue(render.Id, out var seen)
                    ? seen with
                    {
                        Renders = seen.Renders + 1,
                        Ticks = seen.Ticks + render.SelfTicks,
                        MaxTicks = Math.Max(seen.MaxTicks, render.SelfTicks),
                    }
                    : new SlowComponent(render.Id, render.Type, render.Key, 1, render.SelfTicks, render.SelfTicks);
            }
        }

        var list = byId.Values.ToList();
        list.Sort(static (a, b) => a.Ticks != b.Ticks ? b.Ticks.CompareTo(a.Ticks) : a.Id.CompareTo(b.Id));
        return list;
    }

    private static Component SlowestTable(List<SlowComponent> slowest)
    {
        var shown = Math.Min(slowest.Count, SlowestLimit);
        var rows = new Component[shown];
        for (var i = 0; i < shown; i++)
        {
            var item = slowest[i];
            rows[i] = Tr.Key(item.Id)[
                Td[
                    item.Key is null
                        ? Span.Class("font-mono")[item.Type]
                        : Span.Class("whitespace-nowrap")[
                            Span.Class("font-mono")[item.Type], " ", Ui.Badge.Size(Ui.Size.Sm).Mono(true)[item.Key]
                        ]
                ],
                Td.Class("tabular-nums")[Count(item.Renders)],
                Td.Class("tabular-nums whitespace-nowrap font-semibold")[Milliseconds(item.Ticks)],
                Td.Class("tabular-nums whitespace-nowrap")[Milliseconds(item.Ticks / item.Renders)],
                Td.Class("tabular-nums whitespace-nowrap")[Milliseconds(item.MaxTicks)]
            ];
        }

        return Ui.Table.Scroll(true)[
            Thead[Tr[Th["Component"], Th["Renders"], Th["Total"], Th["Average"], Th["Slowest"]]],
            Tbody[rows]
        ];
    }

    /// <summary>The server time at or below which <paramref name="fraction" /> of the interactions fall.</summary>
    internal static long Percentile(DevToolsInteraction[] interactions, double fraction)
    {
        var values = interactions.Select(i => i.ServerTicks).Order().ToArray();
        return values.Length == 0 ? 0 : values[(int)Math.Clamp(Math.Ceiling(fraction * values.Length) - 1, 0, values.Length - 1)];
    }

    private static string PatchPercentile(DevToolsInteraction[] interactions, double fraction)
    {
        var values = interactions.Where(i => i.PatchMilliseconds is not null).Select(i => i.PatchMilliseconds!.Value).Order().ToArray();
        return values.Length == 0
            ? "—"
            : Milliseconds(values[(int)Math.Clamp(Math.Ceiling(fraction * values.Length) - 1, 0, values.Length - 1)]);
    }

    private static string Count(long value) => value.ToString("N0", CultureInfo.InvariantCulture);

    private static string Milliseconds(long ticks) => Milliseconds(Stopwatch.GetElapsedTime(0, ticks).TotalMilliseconds);

    private static string Milliseconds(double milliseconds) =>
        milliseconds.ToString(milliseconds < 10 ? "0.00" : "0.0", CultureInfo.InvariantCulture) + " ms";

    private static string Size(long bytes) => bytes switch
    {
        < 1024 => bytes.ToString(CultureInfo.InvariantCulture) + " B",
        < 1024 * 1024 => (bytes / 1024d).ToString("0.0", CultureInfo.InvariantCulture) + " KB",
        _ => (bytes / (1024d * 1024d)).ToString("0.0", CultureInfo.InvariantCulture) + " MB",
    };
}
