namespace Rask.Example.Site;

// The hero animation: an editor typing `Div.`, the completion list that opens, and the doc comment
// that travels with the highlighted step all the way onto the generated factory parameter. It is the
// "the chain is the documentation" claim, shown instead of asserted.
//
// A component built from the typed SVG family rather than a hand-written .svg file, for the reason
// RaskLogo gives: one source, no asset duplicated across wwwroots. The standalone assets/rask-chain.svg
// the README needs is BAKED from this component and compared byte-for-byte in
// tests/Rask.Example.Site.Tests — so the file can never drift from the component.
//
// Two constraints make one source serve both contexts:
//
//   * Every colour is `var(--token, #literal)`. Rendered inline in the page, the SVG inherits
//     global.css's tokens and follows the site's theme toggle for free; rendered standalone into a
//     file, the custom properties are undefined and every colour falls back to its literal. An
//     <img>-loaded SVG can otherwise only ever follow the OS theme, never the page's.
//   * An SVG <style> inline in an HTML document is NOT scoped to the SVG — its rules apply to the
//     whole page. Hence the `rc-` prefix on every class and keyframe name: without it these would
//     collide with global.css.
//
// Motion is CSS keyframes, matching the boot splash in wwwroot/index.html. It is not a choice:
// Rask.Html ships no <animate>/<animateTransform> component, so SMIL is unavailable. CSS also
// survives being loaded through an <img>, which is how GitHub will render the baked file.
//
// Every element's BASE state is its FINAL frame, so `prefers-reduced-motion: reduce` can simply turn
// the animations off and land on the finished picture — the completion list open, the last step
// highlighted, its summary on screen, and the factory call below it.
internal sealed partial class ChainAnimation : Component
{
    // 21px in a monospace face advances ~12.6px per character, so "Div." ends at 44 + 4 × 12.6 ≈ 94 —
    // where the caret sits and the completion list hangs from. Kept as named constants because the
    // three have to agree; nudging one alone silently detaches the popup from the dot it belongs to.
    private const string CaretX = "94";
    private const string PopupX = "88";

    private const string Css = """
        .rc-win { fill: var(--panel, #14141f); }
        .rc-bar { fill: var(--panel-2, #1b1b2a); }
        .rc-edge { fill: none; stroke: var(--line, rgba(139, 92, 246, 0.28)); stroke-width: 1; }
        .rc-rule { stroke: var(--line, rgba(139, 92, 246, 0.28)); stroke-width: 1; }
        .rc-dot { fill: var(--muted, #8a8aa4); opacity: 0.45; }
        .rc-t {
          font-family: var(--mono, "SFMono-Regular", "JetBrains Mono", ui-monospace, Menlo, Consolas, monospace);
          font-size: 21px;
          fill: var(--ink, #e9e9f2);
        }
        .rc-sm { font-size: 15px; }
        .rc-xs { font-size: 13px; }
        .rc-mut { fill: var(--muted, #8a8aa4); }
        .rc-soft { fill: var(--ink-soft, #c3c2d6); }
        .rc-acc { fill: var(--accent, #8b5cf6); }
        .rc-str { fill: var(--signal, #34d399); }
        .rc-chip { fill: var(--accent, #8b5cf6); opacity: 0.35; }
        /* fill-opacity, not opacity: the same element carries .rc-hl, whose `opacity` the keyframes drive.
           Two `opacity` declarations would have the animation's win and paint a solid block over the row;
           fill-opacity is a separate channel and multiplies with it instead. */
        .rc-hl-bar { fill: var(--accent, #8b5cf6); fill-opacity: 0.18; }

        /* Base state == the final frame, so `animation: none` under reduced motion lands on the payoff. */
        .rc-code { opacity: 1; }
        .rc-caret { fill: var(--accent, #8b5cf6); opacity: 1; }
        .rc-pop { opacity: 1; }
        .rc-hl { opacity: 1; transform: translateY(126px); }
        .rc-d1, .rc-d2, .rc-d3 { opacity: 0; }
        .rc-d4 { opacity: 1; }
        .rc-fac { opacity: 1; }

        @keyframes rc-code { 0% { opacity: 0; } 5%, 100% { opacity: 1; } }
        @keyframes rc-caret { 0%, 45% { opacity: 1; } 55%, 100% { opacity: 0; } }
        @keyframes rc-pop {
          0%, 7% { opacity: 0; transform: translateY(-8px); }
          12%, 100% { opacity: 1; transform: none; }
        }
        @keyframes rc-hl {
          0%, 10% { opacity: 0; transform: translateY(0); }
          14%, 24% { opacity: 1; transform: translateY(0); }
          30%, 42% { opacity: 1; transform: translateY(42px); }
          48%, 60% { opacity: 1; transform: translateY(84px); }
          66%, 100% { opacity: 1; transform: translateY(126px); }
        }
        @keyframes rc-d1 { 0%, 12% { opacity: 0; } 16%, 24% { opacity: 1; } 30%, 100% { opacity: 0; } }
        @keyframes rc-d2 { 0%, 28% { opacity: 0; } 32%, 42% { opacity: 1; } 48%, 100% { opacity: 0; } }
        @keyframes rc-d3 { 0%, 46% { opacity: 0; } 50%, 60% { opacity: 1; } 66%, 100% { opacity: 0; } }
        @keyframes rc-d4 { 0%, 64% { opacity: 0; } 68%, 100% { opacity: 1; } }
        @keyframes rc-fac { 0%, 74% { opacity: 0; } 80%, 100% { opacity: 1; } }

        .rc-code { animation: rc-code 11s infinite; }
        .rc-caret { animation: rc-caret 1.1s steps(1, end) infinite; }
        .rc-pop { animation: rc-pop 11s infinite; }
        .rc-hl { animation: rc-hl 11s infinite; }
        .rc-d1 { animation: rc-d1 11s infinite; }
        .rc-d2 { animation: rc-d2 11s infinite; }
        .rc-d3 { animation: rc-d3 11s infinite; }
        .rc-d4 { animation: rc-d4 11s infinite; }
        .rc-fac { animation: rc-fac 11s infinite; }

        @media (prefers-reduced-motion: reduce) {
          .rc-code, .rc-caret, .rc-pop, .rc-hl,
          .rc-d1, .rc-d2, .rc-d3, .rc-d4, .rc-fac { animation: none; }
        }
        """;

    protected override Component? Render() =>
        Svg
            .ViewBox("0 0 880 400")
            .Xmlns("http://www.w3.org/2000/svg")
            .Class("rc-svg")
            .Role("img")[
                SvgTitle["Chaining onto Div in an editor, with each step's doc comment"],
                Desc[
                    "An editor shows Div.Class(\"card\")[. A completion list offers Class, Style, OnClick and Id; "
                    + "as each is highlighted its XML doc summary appears beside it, and the same summary lands on "
                    + "the generated factory parameter below."
                ],
                SvgStyle[Raw.Value(Css)],
                Window(),
                CodeLine(),
                Popup(),
                DocPane(),
                FactoryLine()
            ];

    // ---- the editor window ----
    private static Component Window() =>
        G[
            Rect.X("0.5").Y("0.5").Width("879").Height("399").Rx("16").Class("rc-win"),
            Rect.X("0.5").Y("0.5").Width("879").Height("44").Rx("16").Class("rc-bar"),
            // The title bar's rounded rect would round its bottom corners too; a square patch over the
            // lower half squares them off again without a clip path (and so without an id to collide).
            Rect.X("0.5").Y("28").Width("879").Height("17").Class("rc-bar"),
            Line.X1("0").Y1("44.5").X2("880").Y2("44.5").Class("rc-rule"),
            Rect.X("0.5").Y("0.5").Width("879").Height("399").Rx("16").Class("rc-edge"),
            Circle.Cx("28").Cy("22").R("5").Class("rc-dot"),
            Circle.Cx("48").Cy("22").R("5").Class("rc-dot"),
            Circle.Cx("68").Cy("22").R("5").Class("rc-dot"),
            SvgText.X("92").Y("27").Class("rc-t rc-xs rc-mut")["ProductCard.cs"]
        ];

    // ---- `Div.Class("card")[` with a blinking caret after the dot ----
    private static Component CodeLine() =>
        G.Class("rc-code")[
            SvgText.X("44").Y("96").Class("rc-t")[
                Tspan.Class("rc-acc")["Div"],
                Tspan[".Class"],
                Tspan.Class("rc-mut")["("],
                Tspan.Class("rc-str")["\"card\""],
                Tspan.Class("rc-mut")[")["]
            ],
            Rect.X(CaretX).Y("78").Width("2").Height("24").Class("rc-caret")
        ];

    // ---- the completion list ----
    private static Component Popup() =>
        G.Class("rc-pop")[
            Rect.X(PopupX).Y("124").Width("300").Height("188").Rx("12").Class("rc-bar"),
            Rect.X(PopupX).Y("124").Width("300").Height("188").Rx("12").Class("rc-edge"),
            Rect.X("94").Y("136").Width("288").Height("38").Rx("8").Class("rc-hl-bar rc-hl"),
            Row("161", "Class", "string?"),
            Row("203", "Style", "string?"),
            Row("245", "OnClick", "Action?"),
            Row("287", "Id", "string?")
        ];

    // `baseline` is the text baseline; the chip sits 13px above it so both centre in the 38px row.
    private static Component Row(string baseline, string name, string type) =>
        G[
            Rect.X("106").Y(Offset(baseline, -13)).Width("14").Height("14").Rx("4").Class("rc-chip"),
            SvgText.X("132").Y(baseline).Class("rc-t rc-sm")[name],
            SvgText.X("372").Y(baseline).TextAnchor("end").Class("rc-t rc-xs rc-mut")[type]
        ];

    private static string Offset(string baseline, int delta) =>
        (int.Parse(baseline, System.Globalization.CultureInfo.InvariantCulture) + delta)
        .ToString(System.Globalization.CultureInfo.InvariantCulture);

    // ---- the doc comment for the highlighted step ----
    private static Component DocPane() =>
        G[
            Rect.X("412").Y("124").Width("432").Height("188").Rx("12").Class("rc-bar"),
            Rect.X("412").Y("124").Width("432").Height("188").Rx("12").Class("rc-edge"),
            Doc(
                "rc-d1",
                "string? Class",
                "The global class attribute — a space-separated",
                "list of class names, the usual hook for CSS",
                "and for finding an element from script."),
            Doc(
                "rc-d2",
                "string? Style",
                "The global style attribute — CSS declarations",
                "applied to this element alone.",
                ""),
            Doc(
                "rc-d3",
                "Action? OnClick",
                "Click. Parameterless (modifier/coordinate-free)",
                "for source compatibility — use the mouse",
                "events below for geometry."),
            Doc(
                "rc-d4",
                "string? Id",
                "The global id attribute — this element's",
                "unique identifier in the document.",
                "")
        ];

    private static Component Doc(string cls, string signature, string line1, string line2, string line3) =>
        G.Class(cls)[
            SvgText.X("436").Y("158").Class("rc-t rc-sm rc-acc")[signature],
            SvgText.X("436").Y("190").Class("rc-t rc-xs rc-mut")["/// <summary>"],
            SvgText.X("436").Y("218").Class("rc-t rc-sm rc-soft")[line1],
            SvgText.X("436").Y("242").Class("rc-t rc-sm rc-soft")[line2],
            SvgText.X("436").Y("266").Class("rc-t rc-sm rc-soft")[line3],
            SvgText.X("436").Y("292").Class("rc-t rc-xs rc-mut")["/// </summary>"]
        ];

    // ---- the payoff: the same summary reaches the generated factory ----
    private static Component FactoryLine() =>
        G.Class("rc-fac")[
            Line.X1("36").Y1("336.5").X2("844").Y2("336.5").Class("rc-rule"),
            SvgText.X("44").Y("372").Class("rc-t rc-sm")[
                Tspan.Class("rc-mut")["Generated."],
                Tspan.Class("rc-acc")["Div"],
                Tspan.Class("rc-mut")["("],
                Tspan["class:"],
                Tspan.Class("rc-str")[" \"card\""],
                Tspan.Class("rc-mut")[")"]
            ],
            SvgText.X("844").Y("372").TextAnchor("end").Class("rc-t rc-xs rc-mut")[
                "the same summary, on the factory parameter"
            ]
        ];
}
