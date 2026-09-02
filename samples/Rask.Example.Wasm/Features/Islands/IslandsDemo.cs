using Rask.Example.Shared;

namespace Rask.Example.Wasm.Features.Islands;

/// <summary>
///     The same islands the Server showcase runs, on the WASM host — from byte-identical front-end
///     files.
/// </summary>
/// <remarks>
///     <para>
///         Nothing in <c>VueChart.vue</c>, <c>ReactCounter.tsx</c> or <c>SvelteMeter.svelte</c> knows
///         which host it is on. On Server a callback rides the live WebSocket; here it is a
///         <c>[JSExport]</c> call straight into this tab's own runtime. The island never opens a
///         channel of its own either way, so it inherits sequence stamping and the
///         queue-while-reconnecting for free.
///     </para>
///     <para>
///         Lit is absent here and that is deliberate rather than an omission: a Lit island's
///         <c>.ts</c> collides with Rask's scoped TypeScript, which this app genuinely uses. The
///         Server showcase, which has no scoped TypeScript at all, carries the Lit case.
///     </para>
/// </remarks>
public sealed partial class IslandsDemo : Component
{
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
    private int _reactTotal;

    protected override Component? Render() =>
    [
        Div.Class($"{Tw.Card} shadow-sm border-0 mb-3")[
            Div.Class(Tw.CardBody)[
                H6.Class("font-bold")["A Vue island calling back into C#, in WebAssembly"],
                P.Class("text-sm text-slate-500 dark:text-slate-400")[
                    "The same ", Code["VueChart.vue"], " the Server showcase builds. Clicking a bar ",
                    "re-enters C# — here through a ", Code["[JSExport]"], " call into this tab's ",
                    "runtime rather than over a socket."
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
                H6.Class("font-bold")["React and Svelte keeping their own state"],
                P.Class("text-sm text-slate-500 dark:text-slate-400")[
                    "Both hold state C# never sees. Raising the reading re-renders this component, and ",
                    "the counters below have to survive it — a remount would reset them, and nothing ",
                    "else on the page would look any different."
                ],

                ReactCounter.Caption("Clicks since mount").Step(_step).OnTotalChanged(TotalChanged),

                Div.Class("mt-3")[
                    SvelteMeter.Value(_reading).Label("Capacity")
                ],

                Div.Class("flex gap-2 mt-3")[
                    Button.Class(Tw.BtnPrimary).Id("island-raise").OnClick(Raise)["Raise the reading"],
                    Button.Class(Tw.BtnOutlinePrimary).Id("island-reset").OnClick(Reset)["Reset"]
                ],

                P.Class("text-sm mt-3 mb-0")[
                    "React reported a total of ",
                    Code.Id("island-react-total")[_reactTotal.ToString()],
                    Span[" back to C#."]
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
        _reactTotal = 0;

        _series.Clear();
        _series.AddRange([new("Jan", 38), new("Feb", 64), new("Mar", 51), new("Apr", 82)]);
    }
}
