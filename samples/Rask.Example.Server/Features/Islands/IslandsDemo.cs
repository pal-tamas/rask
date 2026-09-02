using Rask.Example.Shared;

namespace Rask.Example.Server.Features.Islands;

/// <summary>
///     Two islands driven from C#: a Vue chart that calls back, and a Svelte meter whose own local
///     state has to survive a C# re-render.
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

    private int _reading = 40;
    private int _lastClicked;
    private int _clicks;
    private int _step = 1;
    private int _revision;
    private int _reactTotal;

    protected override Component? Render() =>
    [
        Div.Class($"{Ui.Card} shadow-sm border-0 mb-3")[
            Div.Class(Ui.CardBody)[
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

        Div.Class($"{Ui.Card} shadow-sm border-0 mb-3")[
            Div.Class(Ui.CardBody)[
                H6.Class("font-bold")["A React island, and a Lit one, side by side"],
                P.Class("text-sm text-slate-500 dark:text-slate-400")[
                    "Four runtimes on one page, in one tree. The React counter keeps a ", Code["useState"],
                    " C# never sees; the Lit badge is a custom element whose reactive properties the ",
                    "adapter simply assigns. ", Strong["Preact"], " needs no fifth adapter — it rides this ",
                    "same React one, through an app-wide ", Code["preact/compat"], " alias."
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

        Div.Class($"{Ui.Card} shadow-sm border-0 mb-3")[
            Div.Class(Ui.CardBody)[
                H6.Class("font-bold")["A Svelte island that keeps its own state"],
                P.Class("text-sm text-slate-500 dark:text-slate-400")[
                    "C# owns the reading. The nudge counter belongs to ", Code["SvelteMeter.svelte"],
                    " and C# never sees it — so if raising the reading reset it, the update would be a ",
                    "remount rather than a reconcile."
                ],

                SvelteMeter.Value(_reading).Label("Capacity"),

                Div.Class("flex gap-2 mt-3")[
                    Button.Class(Ui.BtnPrimary).Id("island-raise").OnClick(Raise)["Raise the reading"],
                    Button.Class(Ui.BtnOutlinePrimary).Id("island-reset").OnClick(Reset)["Reset"]
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

    private void Raise()
    {
        _reading = Math.Min(100, _reading + 15);
        _step++;
        _revision++;

        // Nudge the chart too, so ONE C# re-render updates every island's props in the same frame.
        for (var i = 0; i < _series.Count; i++)
        {
            _series[i] = _series[i] with { Value = Math.Min(100, _series[i].Value + 4) };
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

        _series.Clear();
        _series.AddRange([new(Months[0], 38), new(Months[1], 64), new(Months[2], 51), new(Months[3], 82)]);
    }
}
