using Rask.Site;

namespace Rask.Site.Features.Islands;

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
///         <see cref="LitBadge" /> is here BECAUSE this app has scoped TypeScript, not despite it. A
///         Lit island is spelled <c>Name.ts</c> beside <c>Name.cs</c>, which is exactly how a scoped
///         asset is spelled, and until #938 the build could only be told which ONE of the two
///         conventions a project used — so this page could show every runtime but the one with no
///         dependencies. Angular is still absent, for a reason of its own: its toolchain is three npm
///         packages this bundle does not carry.
///     </para>
///     <para>
///         React and Solid sit in folders of their own because they both compile <c>.tsx</c>, so their
///         Vite plugins are each scoped to their own island directory. Sharing one — or nesting one
///         inside the other — is refused by the build rather than mis-compiled.
///     </para>
///     <para>
///         <see cref="ColorPicker" /> is a React component straight from npm, with no <c>.tsx</c> of its own, and it is
///         a CHILD of <see cref="ReactCounter" />: it travels inside the counter's props and React renders both in one
///         tree, so it has no host element of its own and its callback still reaches C#.
///     </para>
/// </remarks>
public sealed partial class IslandsDemo : Component
{
    private const string StartColor = "#c026d3";

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
    private int _reactTotal;
    private int _hoveredPoint = -1;
    private int _badgeNudges;
    private string _color = StartColor;

    protected override Component? Render() =>
    [
        UiCard.Class("shadow-sm mb-3")[
                H6.Class("font-bold")["A Vue island calling back into C#, in WebAssembly"],
                P.Class("text-sm text-ui-muted")[
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
            ],

        UiCard.Class("shadow-sm mb-3")[
                H6.Class("font-bold")["React and Svelte keeping their own state"],
                P.Class("text-sm text-ui-muted")[
                    "Both hold state C# never sees. Raising the reading re-renders this component, and ",
                    "the counters below have to survive it — a remount would reset them, and nothing ",
                    "else on the page would look any different. Inside the React counter is a colour ",
                    "picker straight from npm, nested as a child island: no ", Code[".tsx"], " of its own, ",
                    "its steps generated from the package's TypeScript."
                ],

                ReactCounter.Caption("Clicks since mount").Step(_step).OnTotalChanged(TotalChanged)[
                    "Pick a colour: ",
                    ColorPicker.Color(_color).OnChange(ColorChanged)
                ],

                Div.Class("mt-3")[
                    SvelteMeter.Value(_reading).Label("Capacity")
                ],

                Div.Class("flex gap-2 mt-3")[
                    UiButton.Tone(UiTone.Primary).Id("island-raise").OnClick(Raise)["Raise the reading"],
                    UiButton.Tone(UiTone.Primary).Variant(UiVariant.Outline).Id("island-reset").OnClick(Reset)["Reset"]
                ],

                P.Class("text-sm mt-3 mb-0")[
                    "React reported a total of ",
                    Code.Id("island-react-total")[_reactTotal.ToString()],
                    Span[" back to C#, and the picker the colour "],
                    Code.Id("island-color")[_color],
                    Span["."]
                ]
            ],

        UiCard.Class("shadow-sm mb-3")[
                H6.Class("font-bold")["A Lit island, beside this app's own scoped TypeScript"],
                P.Class("text-sm text-ui-muted")[
                    Code["LitBadge.ts"], " imports nothing at all — a custom element needs no ",
                    "framework and no npm package. It sits in a project that also has scoped ",
                    "TypeScript, which is spelled identically; the build tells them apart by reading ",
                    "the C# base class."
                ],

                LitBadge.Label("Capacity").Value(_reading).OnNudged(BadgeNudged),

                P.Class("text-sm mt-3 mb-0")[
                    "The badge reported ",
                    Code.Id("island-badge-nudges")[_badgeNudges.ToString()],
                    Span[" nudge(s) back to C#."]
                ]
            ],

        UiCard.Class("shadow-sm mb-3")[
                H6.Class("font-bold")["A Solid island, from the same file the Server showcase builds"],
                P.Class("text-sm text-ui-muted")[
                    "Byte-identical to ", Code["SolidSpark.tsx"], " on the Server host. Its hover count ",
                    "belongs to Solid and C# never sees it, so raising the reading has to reach it as a ",
                    "prop change rather than a remount."
                ],

                SolidSpark.Readings(_readings).Caption("Throughput").OnPointHovered(PointHovered),

                P.Class("text-sm mt-3 mb-0")[
                    "Last point hovered: ",
                    Code.Id("island-hovered")[_hoveredPoint < 0 ? "(none)" : _hoveredPoint.ToString()]
                ]
            ]
    ];

    private void BarClicked(int value)
    {
        _lastClicked = value;
        _clicks++;
    }

    private void TotalChanged(int total) => _reactTotal = total;

    private void ColorChanged(string color) => _color = color;

    private void PointHovered(int index) => _hoveredPoint = index;

    private void BadgeNudged(int nudges) => _badgeNudges = nudges;

    private void Raise()
    {
        _reading = Math.Min(100, _reading + 15);
        _step++;

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
        _reactTotal = 0;
        _hoveredPoint = -1;
        _badgeNudges = 0;
        _color = StartColor;

        _readings.Clear();
        _readings.AddRange([12, 30, 22, 48, 35, 61]);

        _series.Clear();
        _series.AddRange([new("Jan", 38), new("Feb", 64), new("Mar", 51), new("Apr", 82)]);
    }
}
