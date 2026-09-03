using Rask.Example.Shared;

namespace Rask.Example.Server.Features.Islands;

/// <summary>
///     Six islands driven from C#, on one page and in one tree: a Vue chart that calls back, a React
///     counter, a Lit badge, a Svelte meter whose own local state has to survive a C# re-render, a
///     Solid sparkline, and an Angular ticker that boots asynchronously.
/// </summary>
/// <remarks>
///     <para>
///         Both sit in ordinary Rask markup, as leaves of this component's tree. That is the whole
///         claim of the feature — replaceability is a property of the COMPONENT, not of the route — so
///         the demo deliberately puts Rask chrome above, below and between them.
///     </para>
///     <para>
///         The interesting half is the meter. Pressing "Raise the reading" re-renders this component
///         with a new <c>Value</c>, which reaches the mounted Svelte component as a prop change rather
///         than a remount: its nudge count keeps climbing. If the adapter ever remounted instead, that
///         count would snap back to zero and nothing else on the page would look any different.
///     </para>
/// </remarks>
public sealed partial class IslandsDemo : Component
{
    private static readonly string[] Months = ["Jan", "Feb", "Mar", "Apr"];

    private readonly List<ChartBar> _series =
    [
        new("Jan", 38),
        new("Feb", 64),
        new("Mar", 51),
        new("Apr", 82),
    ];

    private readonly List<int> _readings = [12, 30, 22, 48, 35, 61];

    private int _reading = 40;
    private int _lastClicked;
    private int _clicks;
    private int _step = 1;
    private int _revision;
    private int _reactTotal;
    private int _hoveredPoint = -1;
    private int _quote = 128;

    protected override Component? Render() =>
    [
        Div.Class($"{Tw.Card} shadow-sm border-0 mb-3")[
            Div.Class(Tw.CardBody)[
                H6.Class("font-bold")["A Vue island that calls back into C#"],
                P.Class("text-sm text-slate-500 dark:text-slate-400")[
                    "The bars are rendered by ", Code["VueChart.vue"],
                    ". Its props are declared in C# and its callback re-enters C# over the same ",
                    "WebSocket every DOM handler uses — the island never opens a channel of its own."
                ],

                VueChart.Series(_series).Heading("Revenue by month").OnBarClick(BarClicked),

                P.Class("text-sm mt-3 mb-0")[
                    "Last bar clicked: ",
                    Code.Id("island-last-clicked")[_lastClicked == 0 ? "(none)" : _lastClicked.ToString()],
                    Span.Class("ms-2")["after "],
                    Code.Id("island-clicks")[_clicks.ToString()],
                    Span[" click(s)"]
                ]
            ]
        ],

        Div.Class($"{Tw.Card} shadow-sm border-0 mb-3")[
            Div.Class(Tw.CardBody)[
                H6.Class("font-bold")["A React island, and a Lit one, side by side"],
                P.Class("text-sm text-slate-500 dark:text-slate-400")[
                    "Six runtimes on one page, in one tree. The React counter keeps a ", Code["useState"],
                    " C# never sees; the Lit badge is a custom element whose reactive properties the ",
                    "adapter simply assigns. ", Strong["Preact"], " is the seventh, and the one that ",
                    "cannot join them here: its Vite plugin and React's pin different major versions of ",
                    "Babel, so npm refuses to install both. It swaps in for React rather than sitting ",
                    "beside it."
                ],

                ReactCounter.Caption("Clicks since mount").Step(_step).OnTotalChanged(TotalChanged),

                Div.Class("mt-3")[
                    LitBadge.Label("live").Revision(_revision)
                ],

                P.Class("text-sm mt-3 mb-0")[
                    "React reported a total of ",
                    Code.Id("island-react-total")[_reactTotal.ToString()],
                    Span[" back to C#."]
                ]
            ]
        ],

        Div.Class($"{Tw.Card} shadow-sm border-0 mb-3")[
            Div.Class(Tw.CardBody)[
                H6.Class("font-bold")["A Svelte island that keeps its own state"],
                P.Class("text-sm text-slate-500 dark:text-slate-400")[
                    "C# owns the reading. The nudge counter belongs to ", Code["SvelteMeter.svelte"],
                    " and C# never sees it — so if raising the reading reset it, the update would be a ",
                    "remount rather than a reconcile."
                ],

                SvelteMeter.Value(_reading).Label("Capacity"),

                Div.Class("flex gap-2 mt-3")[
                    Button.Class(Tw.BtnPrimary).Id("island-raise").OnClick(Raise)["Raise the reading"],
                    Button.Class(Tw.BtnOutlinePrimary).Id("island-reset").OnClick(Reset)["Reset"]
                ]
            ]
        ],

        Div.Class($"{Tw.Card} shadow-sm border-0 mb-3")[
            Div.Class(Tw.CardBody)[
                H6.Class("font-bold")["A Solid island, in a folder of its own"],
                P.Class("text-sm text-slate-500 dark:text-slate-400")[
                    "Solid compiles ", Code[".tsx"], " and so does React, so each one's Vite plugin is ",
                    "scoped to the directory its own islands live in. That is why this component sits ",
                    "under ", Code["Features/Islands/Solid/"], " — sharing a folder would leave both ",
                    "plugins claiming the same files, and the loser's island would be compiled with the ",
                    "wrong JSX transform. The build refuses that rather than shipping it."
                ],

                SolidSpark.Readings(_readings).Caption("Throughput").OnPointHovered(PointHovered),

                P.Class("text-sm mt-3 mb-0")[
                    "Last point hovered: ",
                    Code.Id("island-hovered")[_hoveredPoint < 0 ? "(none)" : _hoveredPoint.ToString()]
                ]
            ]
        ],

        Div.Class($"{Tw.Card} shadow-sm border-0 mb-3")[
            Div.Class(Tw.CardBody)[
                H6.Class("font-bold")["An Angular island, which boots asynchronously"],
                P.Class("text-sm text-slate-500 dark:text-slate-400")[
                    "The only runtime here whose bootstrap returns a promise. Props that arrive before ",
                    "it resolves are held and applied on arrival rather than dropped — so pressing ",
                    Strong["Raise the reading"], " on a cold page still shows the quote C# last sent, ",
                    "not the one it sent first."
                ],

                AngularTicker.Symbol("RSK").Quote(_quote).OnRefreshRequested(RefreshRequested),

                P.Class("text-sm mt-3 mb-0")[
                    "C# has moved the quote to ",
                    Code.Id("island-quote")[_quote.ToString()],
                    Span[" on request."]
                ]
            ]
        ]
    ];

    private void BarClicked(int value)
    {
        _lastClicked = value;
        _clicks++;
    }

    private void TotalChanged(int total) => _reactTotal = total;

    private void PointHovered(int index) => _hoveredPoint = index;

    private void RefreshRequested() => _quote += 7;

    private void Raise()
    {
        _reading = Math.Min(100, _reading + 15);
        _step++;
        _revision++;

        _quote += 3;

        // Nudge the chart too, so ONE C# re-render updates every island's props in the same frame.
        for (var i = 0; i < _series.Count; i++)
        {
            _series[i] = _series[i] with { Value = Math.Min(100, _series[i].Value + 4) };
        }

        for (var i = 0; i < _readings.Count; i++)
        {
            _readings[i] = Math.Min(100, _readings[i] + 5);
        }
    }

    private void Reset()
    {
        _reading = 40;
        _lastClicked = 0;
        _clicks = 0;
        _step = 1;
        _revision = 0;
        _reactTotal = 0;
        _hoveredPoint = -1;
        _quote = 128;

        _readings.Clear();
        _readings.AddRange([12, 30, 22, 48, 35, 61]);

        _series.Clear();
        _series.AddRange([new(Months[0], 38), new(Months[1], 64), new(Months[2], 51), new(Months[3], 82)]);
    }
}
