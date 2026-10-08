using System.Diagnostics;
using System.Globalization;
using Rask.Core;
using Rask.DevTools.Probe;

namespace Rask.DevTools.Panel;

/// <summary>
///     The Renders tab: which components of the inspected page actually ran <c>Render()</c>, how often and why — totalled per
///     component, or commit by commit.
/// </summary>
/// <remarks>
///     <para>
///         Only real renders count. A component the walk served from its render cache did no work and is not listed, which
///         is what makes a component that renders on every click stand out.
///     </para>
///     <para>
///         The totals are worked out from the commits the feed holds (see <see cref="DevToolsFeed.CommitCapacity" />), so
///         they cover the page's recent past rather than its whole life, and Clear starts them again from nothing.
///     </para>
/// </remarks>
internal sealed partial class DevToolsRendersTab : Component
{
    /// <summary>How many rows either view lists. The totals above count everything held.</summary>
    internal const int RowLimit = 200;

    // How many components a commit's row names before it says how many more there were.
    private const int NamesPerCommit = 8;

    private DevToolsFeed? _following;
    private DevToolsRefreshGate? _gate;
    private bool _byCommit;

    /// <summary>The colour the page flashes a component that rendered in; the host's stylesheet uses the same.</summary>
    internal const string RenderColour = "#f59e0b";

    /// <summary>The colour the page flashes a node the patch changed.</summary>
    internal const string DomColour = "#14b8a6";

    /// <summary>The inspected session's feed.</summary>
    public required DevToolsFeed Feed { get; set; }

    /// <summary>Whether the page flashes renders and DOM changes. Held by the panel page, so it outlives this tab.</summary>
    public bool? Flash { get; set; }

    /// <summary>Raised when the flash switch is flipped.</summary>
    public Callback<bool> OnFlash { get; set; }

    // The view switch is a field, which the render cache cannot see.
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
        var commits = Feed.CommitsSnapshot();
        var stats = Totals(commits, out var renders, out var ticks);

        return Div.Class("flex flex-col gap-3")[
            Div.Class("flex flex-wrap items-center justify-between gap-2")[
                Ui.ButtonGroup[
                    ViewButton("By component", byCommit: false),
                    ViewButton("By commit", byCommit: true)
                ],
                Div.Class("flex flex-wrap items-center gap-3")[
                    Ui.Switch.Value(Flash ?? false).OnChange(OnFlash).Label("Flash on the page").Left,
                    Flash == true
                        ? Span.Class("flex items-center gap-3 text-xs")[
                            Swatch(RenderColour, "rendered"),
                            Swatch(DomColour, "changed in the DOM")
                        ]
                        : null,
                    Ui.Button.Sm.Title("Forget the renders counted so far").OnClick(Feed.ClearCommits)["Clear"]
                ]
            ],
            commits.Length == 0
                ? Ui.Callout.Secondary.Heading("No renders yet.").Text("Use the page, and every component that renders is counted here, with why.")
                : Counted(commits, stats, renders, ticks)
        ];
    }

    private Component Counted(DevToolsCommit[] commits, List<ComponentStat> stats, int renders, long ticks) =>
        Div.Class("flex flex-col gap-3")[
            Ui.MetricRow[
                Ui.Metric.Label("Commits").Value(Count(commits.Length)),
                Ui.Metric.Label("Renders").Value(Count(renders)),
                Ui.Metric.Label("Components").Value(Count(stats.Count)),
                Ui.Metric.Label("Render time").Value(Milliseconds(ticks))
            ],
            _byCommit ? CommitTable(commits) : ComponentTable(stats)
        ];

    private void OnFeedChanged(object? sender, EventArgs e) => _gate?.Notify();

    private Component ViewButton(string label, bool byCommit) =>
        Ui.Button.Sm
            .Variant(_byCommit == byCommit ? Ui.ButtonVariant.Primary : Ui.ButtonVariant.Outline)
            .AriaPressed(_byCommit == byCommit ? AriaPressed.True : AriaPressed.False)
            .OnClick(() => _byCommit = byCommit)[label];

    /// <summary>One component instance's renders across the commits held.</summary>
    internal sealed class ComponentStat(long id, string type, string? key)
    {
        internal long Id { get; } = id;
        internal string Type { get; } = type;
        internal string? Key { get; } = key;
        internal int Renders { get; set; }
        internal long Ticks { get; set; }
        internal long LastCommit { get; set; }
        internal int[] ReasonCounts { get; } = new int[Enum.GetValues<DevToolsRenderReason>().Length];
    }

    /// <summary>Totals per component instance, most renders first, then most time, then the most recently rendered.</summary>
    internal static List<ComponentStat> Totals(DevToolsCommit[] commits, out int renders, out long ticks)
    {
        var byId = new Dictionary<long, ComponentStat>();
        renders = 0;
        ticks = 0;
        foreach (var commit in commits)
        {
            foreach (var render in commit.Renders)
            {
                if (!byId.TryGetValue(render.Id, out var stat))
                {
                    byId[render.Id] = stat = new ComponentStat(render.Id, render.Type, render.Key);
                }

                stat.Renders++;
                stat.ReasonCounts[(int)render.Reason]++;
                stat.LastCommit = commit.Sequence;
                renders++;
                if (render.SelfTicks > 0)
                {
                    stat.Ticks += render.SelfTicks;
                    ticks += render.SelfTicks;
                }
            }
        }

        var list = byId.Values.ToList();
        list.Sort(static (a, b) => (a.Renders != b.Renders, a.Ticks != b.Ticks) switch
        {
            (true, _) => b.Renders.CompareTo(a.Renders),
            (_, true) => b.Ticks.CompareTo(a.Ticks),
            _ => b.LastCommit.CompareTo(a.LastCommit),
        });
        return list;
    }

    private static Component ComponentTable(List<ComponentStat> stats)
    {
        var shown = Math.Min(stats.Count, RowLimit);
        var rows = new Component[shown];
        for (var i = 0; i < shown; i++)
        {
            var stat = stats[i];
            rows[i] = Ui.TableRow.Key(stat.Id)[
                Ui.TableCell[Name(stat.Type, stat.Key)],
                Ui.TableCell.Class("tabular-nums")[Count(stat.Renders)],
                Ui.TableCell[Reasons(stat.ReasonCounts)],
                Ui.TableCell.Class("tabular-nums whitespace-nowrap")[Milliseconds(stat.Ticks)],
                Ui.TableCell.Class("tabular-nums opacity-60")[stat.LastCommit.ToString(CultureInfo.InvariantCulture)]
            ];
        }

        return Div.Class("flex flex-col gap-3")[
            P.Class("text-xs opacity-60")[
                shown == stats.Count
                    ? "Most renders first. Time is each component's own Render(), not its children's."
                    : $"The {Count(shown)} components with the most renders, of {Count(stats.Count)}."
            ],
            Ui.Table[
                Ui.TableColumns[Ui.TableColumn["Component"], Ui.TableColumn["Renders"], Ui.TableColumn["Why"], Ui.TableColumn["Time"], Ui.TableColumn["Last commit"]],
                Ui.TableRows[rows]
            ]
        ];
    }

    private static Component CommitTable(DevToolsCommit[] commits)
    {
        var shown = Math.Min(commits.Length, RowLimit);
        var rows = new Component[shown];
        for (var i = 0; i < shown; i++)
        {
            var at = commits.Length - 1 - i;
            var commit = commits[at];
            long ticks = 0;
            foreach (var render in commit.Renders)
            {
                ticks += Math.Max(0, render.SelfTicks);
            }

            rows[i] = Ui.TableRow.Key(commit.Sequence)[
                Ui.TableCell.Class("tabular-nums opacity-60")[commit.Sequence.ToString(CultureInfo.InvariantCulture)],
                Ui.TableCell.Class("tabular-nums whitespace-nowrap")[$"{Count(commit.Renders.Length)} of {Count(commit.Walked)}"],
                Ui.TableCell[Rendered(commit)],
                Ui.TableCell.Class("tabular-nums whitespace-nowrap")[Milliseconds(ticks)],
                Ui.TableCell.Class("tabular-nums whitespace-nowrap opacity-60")[
                    at > 0 ? Gap(commits[at - 1].Timestamp, commit.Timestamp) : ""
                ]
            ];
        }

        return Div.Class("flex flex-col gap-3")[
            P.Class("text-xs opacity-60")[
                shown == commits.Length
                    ? "Newest first. Rendered counts the components that ran Render(), of all the render walked."
                    : $"The newest {Count(shown)} of {Count(commits.Length)} commits."
            ],
            Ui.Table[
                Ui.TableColumns[Ui.TableColumn["#"], Ui.TableColumn["Rendered"], Ui.TableColumn["Components"], Ui.TableColumn["Time"], Ui.TableColumn["Gap"]],
                Ui.TableRows[rows]
            ]
        ];
    }

    // The components that rendered in one commit, in the order they rendered, each with why — repeats of one type and
    // reason folded together, so a list of forty rows reads as one entry.
    private static Component Rendered(DevToolsCommit commit)
    {
        if (commit.Renders.Length == 0)
        {
            return Span.Class("opacity-60")["nothing: every component was served from its cache"];
        }

        var groups = new List<(string Type, DevToolsRenderReason Reason, int Count)>();
        foreach (var render in commit.Renders)
        {
            var index = groups.FindIndex(g => string.Equals(g.Type, render.Type, StringComparison.Ordinal) && g.Reason == render.Reason);
            if (index >= 0)
            {
                groups[index] = groups[index] with { Count = groups[index].Count + 1 };
            }
            else
            {
                groups.Add((render.Type, render.Reason, 1));
            }
        }

        var shown = Math.Min(groups.Count, NamesPerCommit);
        var items = new List<Component>(shown + 1);
        for (var i = 0; i < shown; i++)
        {
            var (type, reason, count) = groups[i];
            items.Add(Span.Key(i).Class("whitespace-nowrap")[
                Span.Class("font-mono")[count > 1 ? $"{type} ×{count}" : type],
                " ",
                ReasonBadge(reason, null)
            ]);
        }

        if (groups.Count > shown)
        {
            items.Add(Span.Key("more").Class("opacity-60")[$"and {Count(groups.Count - shown)} more"]);
        }

        return Div.Class("flex flex-wrap items-center gap-2")[items];
    }

    // A legend entry: the outline the page draws, in its colour, and what it means.
    private static Component Swatch(string colour, string meaning) =>
        Span.Class("flex items-center gap-1 whitespace-nowrap")[
            Span.Style($"display:inline-block;width:.75rem;height:.75rem;border:2px solid {colour};border-radius:2px"),
            meaning
        ];

    private static Component Name(string type, string? key) =>
        key is null
            ? Span.Class("font-mono")[type]
            : Span.Class("whitespace-nowrap")[Span.Class("font-mono")[type], " ", Ui.Badge.Sm.Class("font-mono max-w-full break-all whitespace-normal!")[key]];

    private static Component Reasons(int[] counts)
    {
        var badges = new List<Component>();
        for (var i = 0; i < counts.Length; i++)
        {
            if (counts[i] > 0)
            {
                badges.Add(ReasonBadge((DevToolsRenderReason)i, counts[i]));
            }
        }

        return Div.Class("flex flex-wrap items-center gap-1")[badges];
    }

    private static Component ReasonBadge(DevToolsRenderReason reason, int? count) =>
        Ui.Badge
            .Key((int)reason)
            .Sm
            .Color(reason == DevToolsRenderReason.Mount ? Ui.Color.Blue : null)
            .Title(Explain(reason))[count is { } n and > 1 ? $"{DevToolsNames.Label(reason)} ×{n}" : DevToolsNames.Label(reason)];

    private static string Explain(DevToolsRenderReason reason) => reason switch
    {
        DevToolsRenderReason.Mount => "Its first render.",
        DevToolsRenderReason.Props => "Its parent passed props that changed.",
        DevToolsRenderReason.State => "StateHasChanged, or a handler of its own ran.",
        DevToolsRenderReason.Bypass => "It overrides BypassRenderCache, so it renders whenever its parent does.",
        DevToolsRenderReason.Context => "It read context or culture, so it renders whenever its parent does.",
        DevToolsRenderReason.Children => "It takes children, so it renders whenever its parent does.",
        _ => "Nothing marked it, and it had no cached render: it renders null, or its output was kept as frames.",
    };

    private static string Count(long value) => value.ToString("N0", CultureInfo.InvariantCulture);

    private static string Milliseconds(long ticks)
    {
        var milliseconds = Stopwatch.GetElapsedTime(0, ticks).TotalMilliseconds;
        return milliseconds.ToString(milliseconds < 10 ? "0.00" : "0.0", CultureInfo.InvariantCulture) + " ms";
    }

    private static string Gap(long from, long to)
    {
        var milliseconds = Stopwatch.GetElapsedTime(from, to).TotalMilliseconds;
        return milliseconds.ToString(milliseconds < 10 ? "0.0" : "0", CultureInfo.InvariantCulture) + " ms";
    }
}
