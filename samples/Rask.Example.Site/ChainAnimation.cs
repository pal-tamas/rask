using System.Globalization;
using System.Text;

namespace Rask.Example.Site;

// The hero animation: the README's Counter component typed out a character at a time, with the IDE's
// hints arriving mid-flight — the indexer tooltip as `H1[` is written, then the member list and its doc
// comment when the caret stops after `Button.`. It is the "the chain is the documentation" claim shown
// instead of asserted, and it types the whole example so the shape of a real Rask component is on screen.
//
// A component built from the typed SVG family rather than a hand-written .svg file, for the reason
// RaskLogo gives: one source, no asset duplicated across wwwroots. The standalone assets/rask-chain.svg
// the README needs is BAKED from this component and compared byte-for-byte in
// tests/Rask.Example.Site.Tests — so the file can never drift from the component.
//
// Three constraints shape it:
//
//   * Every colour is `var(--token, #literal)`. Rendered inline in the page the SVG inherits global.css's
//     tokens and follows the site's theme toggle for free; rendered standalone into a file the custom
//     properties are undefined and every colour falls back to its literal. An <img>-loaded SVG can
//     otherwise only ever follow the OS theme, never the page's.
//   * An SVG <style> inline in an HTML document is NOT scoped to the SVG — its rules apply to the whole
//     page. Hence the `rc-` prefix on every class and keyframe name; a test enforces it.
//   * Motion is CSS keyframes. That is not a preference: Rask.Html ships no <animate>/<animateTransform>,
//     so SMIL is unavailable, and CSS is also what survives being loaded through an <img>, which is how
//     GitHub renders the baked file.
//
// The typing is a cover rectangle per segment, painted the window colour and scaled down to nothing from
// its right edge (`transform-origin: 100%`), with `steps(n)` where n is the segment's character count —
// so the reveal lands exactly on character boundaries. It is scaleX rather than a translate because a
// translated cover would slide right and eat the NEXT segment on the same line: `Button.` and the rest of
// its line are two segments sharing a baseline, and the popup opens between them.
//
// Every element's BASE state is its FINAL frame — code fully typed, hints dismissed — so
// `prefers-reduced-motion: reduce` can simply turn the animations off and land on the finished component
// rather than on a blank window.
internal sealed partial class ChainAnimation : Component
{
    private const double Loop = 16;    // seconds; one full type-through
    private const double Adv = 10.8;   // monospace advance at font-size 18 (0.6em)
    private const double LineH = 28;
    private const double CodeX = 44;
    private const double Top = 86;     // baseline of line 0

    // Where the member list hangs from: the caret after `        Button.`, 15 characters in.
    private const double PopupX = CodeX + (15 * Adv);
    private const double PopupY = 352;
    private const double PopupW = 380;
    private const double RowH = 28;
    private const double RowsY = 360;

    private enum Tk
    {
        Plain,
        Kw,
        Ent,
        Mem,
        Str,
        Mut,
    }

    private readonly record struct Tok(string Text, Tk Kind);

    private static Tok Kw(string t) => new(t, Tk.Kw);

    private static Tok En(string t) => new(t, Tk.Ent);

    private static Tok Me(string t) => new(t, Tk.Mem);

    private static Tok St(string t) => new(t, Tk.Str);

    private static Tok Mu(string t) => new(t, Tk.Mut);

    private static Tok Pl(string t) => new(t, Tk.Plain);

    // One typed run. `Col` is the character column it starts at, so line 9 can be two segments — the
    // caret stops after `Button.` for the completion list, then finishes the line.
    private sealed record Seg(int Line, int Col, double Start, double End, Tok[] Toks)
    {
        public int Chars => Toks.Sum(t => t.Text.Length);

        public double Width => Chars * Adv;

        public double X => CodeX + (Col * Adv);

        public double Baseline => Top + (Line * LineH);
    }

    // The README's Counter, verbatim. Percentages are of the 16s loop; the gaps between segments are the
    // pauses that make it read as typing rather than as a progress bar.
    //
    // Indentation is the segment's Col, never leading spaces in the text: an SVG <text> collapses runs of
    // whitespace and strips leading whitespace outright (xml:space defaults to "default"), so indented
    // source typed as literal spaces loses its shape — and with textLength set, the stretch to fill the
    // declared width then spreads every glyph apart. Col is exact and costs nothing.
    private static readonly Seg[] Lines =
    [
        new(0, 0, 1.5, 8.5, [
            Kw("public"), Pl(" "), Kw("sealed"), Pl(" "), Kw("partial"), Pl(" "), Kw("class"), Pl(" "),
            Pl("Counter"), Mu(" : "), Pl("Page")
        ]),
        new(1, 0, 8.5, 9, [Mu("{")]),
        new(2, 4, 9.5, 17.5, [
            Kw("protected"), Pl(" "), Kw("override"), Pl(" "), Kw("string"), Pl(" "),
            Me("Route"), Pl(" "), Mu("=>"), Pl(" "), St("\"/counter\""), Mu(";")
        ]),
        new(3, 4, 18, 22, [
            Kw("private"), Pl(" "), Kw("int"), Pl(" "), Pl("_count"), Mu(";")
        ]),
        new(5, 4, 22.5, 30, [
            Kw("protected"), Pl(" "), Kw("override"), Pl(" "), Pl("Component?"), Pl(" "),
            Me("Render"), Mu("()"), Pl(" "), Mu("=>")
        ]),
        new(6, 4, 30, 31, [Mu("[")]),
        new(7, 8, 31.5, 35.5, [
            En("H1"), Mu("["), St("\"Counter\""), Mu("]"), Mu(",")
        ]),
        new(8, 8, 42.5, 49, [
            En("P"), Mu("["), St("$\"Current count: {_count}\""), Mu("]"), Mu(",")
        ]),
        // The caret stops here — this is where the member list opens.
        new(9, 8, 49.5, 53, [En("Button"), Mu(".")]),
        new(9, 15, 71.5, 77.5, [
            Me("OnClick"), Mu("(()"), Pl(" "), Mu("=>"), Pl(" "), Pl("_count"), Mu("++)"), Mu("["),
            St("\"Click me\""), Mu("]")
        ]),
        new(10, 4, 78, 79, [Mu("];")]),
        new(11, 0, 79.5, 80, [Mu("}")]),
    ];

    // The completion list, alphabetical the way an IDE orders it. Summaries are the real XML doc comments
    // off Element / ElementEvents, trimmed to their first sentence — quoting them is the whole point.
    private static readonly (string Name, string Type, string[] Doc)[] Members =
    [
        ("Class", "string?", [
            "The global class attribute — a space-separated",
            "list of class names, the usual hook for CSS",
            "and for finding an element from script."
        ]),
        ("Id", "string?", [
            "The global id attribute — this element's",
            "unique identifier in the document.",
            ""
        ]),
        ("OnClick", "Action?", [
            "Click. Parameterless (modifier/coordinate-free)",
            "for source compatibility — use the mouse",
            "events below for geometry."
        ]),
        ("Style", "string?", [
            "The global style attribute — CSS declarations",
            "applied to this element alone.",
            ""
        ]),
    ];

    protected override Component? Render()
    {
        var children = new List<Component>
        {
            SvgTitle["The Counter component being typed, with the IDE's completion list and doc comment"],
            Desc[
                "An editor types a Rask Counter page character by character. As H1[ is written a tooltip "
                + "explains the indexer; when the caret stops after Button. a completion list opens showing "
                + "Class, Id, OnClick and Style with each member's XML doc summary, then the line finishes "
                + "as Button.OnClick(() => _count++)[\"Click me\"]."
            ],
            SvgStyle[Raw.Value(Css())],
            Window(),
        };

        children.AddRange(Lines.Select((s, i) => CodeText(s, i)));
        children.Add(Closing());
        children.Add(IndexerHint());
        // Last, so it paints over the closing line it briefly shares space with.
        children.Add(Popup());

        return Svg
            .ViewBox("0 0 880 580")
            .Xmlns("http://www.w3.org/2000/svg")
            .Class("rc-svg")
            .Role("img")[children];
    }

    // ---- the editor window ----
    private static Component Window() =>
        G[
            Rect.X("0.5").Y("0.5").Width("879").Height("579").Rx("16").Class("rc-win"),
            Rect.X("0.5").Y("0.5").Width("879").Height("44").Rx("16").Class("rc-bar"),
            // The title bar's rounded rect would round its bottom corners too; a square patch over the
            // lower half squares them off again without a clip path (and so without an id to collide).
            Rect.X("0.5").Y("28").Width("879").Height("17").Class("rc-bar"),
            Line.X1("0").Y1("44.5").X2("880").Y2("44.5").Class("rc-rule"),
            Rect.X("0.5").Y("0.5").Width("879").Height("579").Rx("16").Class("rc-edge"),
            Circle.Cx("28").Cy("22").R("5").Class("rc-dot"),
            Circle.Cx("48").Cy("22").R("5").Class("rc-dot"),
            Circle.Cx("68").Cy("22").R("5").Class("rc-dot"),
            SvgText.X("92").Y("27").Class("rc-t rc-xs rc-mut")["Counter.cs"],
            SvgText.X("856").Y("27").TextAnchor("end").Class("rc-t rc-xs rc-mut")[
                "no .razor · no JavaScript · press . and the chain tells you the rest"
            ]
        ];

    // ---- one typed segment: the text, the cover that reveals it, the caret at the boundary ----
    private static Component CodeText(Seg seg, int index)
    {
        var x = Num(seg.X);
        var baseline = Num(seg.Baseline);

        var text = SvgText
            .X(x)
            .Y(baseline)
            .TextLength(Num(seg.Width))
            .LengthAdjust("spacing")
            .Class("rc-t")[seg.Toks.Select(Span).ToList()];

        return G.Key($"seg{index.ToString(CultureInfo.InvariantCulture)}")[
            text,
            // Painted the window colour and scaled away to the right, so the text appears left-to-right.
            Rect
                .X(x)
                .Y(Num(seg.Baseline - 20))
                .Width(Num(seg.Width))
                .Height("26")
                .Class($"rc-win rc-cv rc-cv{index.ToString(CultureInfo.InvariantCulture)}"),
            Rect
                .X(x)
                .Y(Num(seg.Baseline - 19))
                .Width("2")
                .Height("24")
                .Class($"rc-ct rc-ct{index.ToString(CultureInfo.InvariantCulture)}")
        ];
    }

    private static Component Span(Tok tok) =>
        tok.Kind == Tk.Plain
            ? Tspan[tok.Text]
            : Tspan.Class(ClassFor(tok.Kind))[tok.Text];

    private static string ClassFor(Tk kind) => kind switch
    {
        Tk.Kw => "rc-kw",
        Tk.Ent => "rc-ent",
        Tk.Mem => "rc-mem",
        Tk.Str => "rc-str",
        Tk.Mut => "rc-mut",
        _ => "rc-t",
    };

    // ---- the payoff, once the component is written ----
    private static Component Closing() =>
        G.Class("rc-end")[
            Line.X1("44").Y1("460.5").X2("836").Y2("460.5").Class("rc-rule"),
            SvgText.X("44").Y("492").Class("rc-t rc-sm rc-soft")[
                Tspan.Class("rc-mut")["/// "],
                Tspan["every step is a property, and its summary follows it onto the generated factory"]
            ]
        ];

    // ---- the first hint: what the [ … ] after H1 actually is ----
    private static Component IndexerHint() =>
        G.Class("rc-hint")[
            // 376 wide, not 330: the label is 38 monospace characters at 15px (9px advance) plus 16px of
            // padding either side, and a narrower box lets "here" hang outside the rounded rect.
            Rect.X("163").Y("292").Width("376").Height("36").Rx("8").Class("rc-bar"),
            Rect.X("163").Y("292").Width("376").Height("36").Rx("8").Class("rc-edge"),
            SvgText.X("179").Y("315").Class("rc-t rc-sm rc-soft")[
                Tspan.Class("rc-mut")["[ … ]"],
                Tspan[" is the indexer — children go here"]
            ]
        ];

    // ---- the second hint: the member list, and the doc comment that comes with each step ----
    private static Component Popup()
    {
        var children = new List<Component>
        {
            Rect.X(Num(PopupX)).Y(Num(PopupY)).Width(Num(PopupW)).Height("206").Rx("10").Class("rc-bar"),
            Rect.X(Num(PopupX)).Y(Num(PopupY)).Width(Num(PopupW)).Height("206").Rx("10").Class("rc-edge"),
            Rect
                .X(Num(PopupX + 4))
                .Y(Num(RowsY))
                .Width(Num(PopupW - 8))
                .Height(Num(RowH))
                .Rx("6")
                .Class("rc-hlbar rc-hl"),
        };

        for (var i = 0; i < Members.Length; i++)
        {
            var (name, type, _) = Members[i];
            var rowY = RowsY + (i * RowH);
            children.Add(Rect
                .Key($"chip{name}")
                .X(Num(PopupX + 10))
                .Y(Num(rowY + 8))
                .Width("12")
                .Height("12")
                .Rx("3")
                .Class("rc-chip"));
            children.Add(SvgText
                .Key($"name{name}")
                .X(Num(PopupX + 32))
                .Y(Num(rowY + 19))
                .Class("rc-t rc-sm")[name]);
            children.Add(SvgText
                .Key($"type{name}")
                .X(Num(PopupX + PopupW - 14))
                .Y(Num(rowY + 19))
                .TextAnchor("end")
                .Class("rc-t rc-xs rc-mut")[type]);
        }

        children.Add(Line
            .X1(Num(PopupX + 10))
            .Y1("478.5")
            .X2(Num(PopupX + PopupW - 10))
            .Y2("478.5")
            .Class("rc-rule"));

        // One doc block per member; only the highlighted one is visible at a time.
        for (var i = 0; i < 3; i++)
        {
            var (_, _, doc) = Members[i];
            var block = new List<Component>
            {
                SvgText.X(Num(PopupX + 14)).Y("496").Class("rc-t rc-xs rc-mut")["/// <summary>"],
            };
            for (var l = 0; l < doc.Length; l++)
            {
                block.Add(SvgText
                    .Key($"doc{i.ToString(CultureInfo.InvariantCulture)}-{l.ToString(CultureInfo.InvariantCulture)}")
                    .X(Num(PopupX + 14))
                    .Y(Num(510 + (l * 16)))
                    .Class("rc-t rc-xxs rc-soft")[doc[l]]);
            }

            children.Add(G
                .Key($"doc{i.ToString(CultureInfo.InvariantCulture)}")
                .Class($"rc-doc rc-doc{i.ToString(CultureInfo.InvariantCulture)}")[block]);
        }

        return G.Class("rc-pop")[children];
    }

    private static string Num(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    private static string Pct(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    // ---- the stylesheet, generated so the timing maths is written once ----
    private static string Css()
    {
        var sb = new StringBuilder();

        sb.Append("""
            .rc-win { fill: var(--panel, #14141f); }
            .rc-bar { fill: var(--panel-2, #1b1b2a); }
            .rc-edge { fill: none; stroke: var(--line, rgba(139, 92, 246, 0.28)); stroke-width: 1; }
            .rc-rule { stroke: var(--line, rgba(139, 92, 246, 0.28)); stroke-width: 1; }
            .rc-dot { fill: var(--muted, #8a8aa4); opacity: 0.45; }
            .rc-t {
              font-family: var(--mono, "SFMono-Regular", "JetBrains Mono", ui-monospace, Menlo, Consolas, monospace);
              font-size: 18px;
              fill: var(--ink, #e9e9f2);
            }
            .rc-sm { font-size: 15px; }
            .rc-xs { font-size: 13px; }
            .rc-xxs { font-size: 12px; }
            .rc-mut { fill: var(--muted, #8a8aa4); }
            .rc-soft { fill: var(--ink-soft, #c3c2d6); }
            .rc-kw { fill: var(--accent-ink, #c4b5fd); }
            .rc-ent { fill: var(--accent, #8b5cf6); }
            .rc-mem { fill: var(--ink, #e9e9f2); }
            .rc-str { fill: var(--signal, #34d399); }
            .rc-chip { fill: var(--accent, #8b5cf6); opacity: 0.35; }
            /* fill-opacity, not opacity: .rc-hl on the same element has its opacity driven by keyframes,
               and two opacity declarations would let the animation paint a solid block over the row. */
            .rc-hlbar { fill: var(--accent, #8b5cf6); fill-opacity: 0.18; }
            .rc-cv { transform-box: fill-box; transform-origin: 100% 50%; }
            .rc-ct { fill: var(--accent, #8b5cf6); }

            /* Base state == the final frame: everything typed, every hint dismissed. */
            .rc-cv { transform: scaleX(0); }
            .rc-ct { opacity: 0; }
            .rc-hint { opacity: 0; }
            .rc-pop { opacity: 0; }
            .rc-hl { opacity: 0; }
            .rc-doc { opacity: 0; }
            .rc-end { opacity: 1; }

            """);

        for (var i = 0; i < Lines.Length; i++)
        {
            var seg = Lines[i];
            var n = seg.Chars.ToString(CultureInfo.InvariantCulture);
            var id = i.ToString(CultureInfo.InvariantCulture);
            var loop = Num(Loop);

            sb.Append(CultureInfo.InvariantCulture, $".rc-cv{id} {{ animation: rc-cv{id} {loop}s steps({n}, end) infinite; }}\n");
            sb.Append(CultureInfo.InvariantCulture, $"@keyframes rc-cv{id} {{ 0%, {Pct(seg.Start)}% {{ transform: scaleX(1); }} {Pct(seg.End)}%, 100% {{ transform: scaleX(0); }} }}\n");

            sb.Append(CultureInfo.InvariantCulture, $".rc-ct{id} {{ animation: rc-ct{id} {loop}s steps({n}, end) infinite; }}\n");
            sb.Append(CultureInfo.InvariantCulture,
                $"@keyframes rc-ct{id} {{ 0%, {Pct(seg.Start)}% {{ transform: translateX(0); opacity: 0; }} " +
                $"{Pct(seg.Start + 0.2)}% {{ opacity: 1; }} " +
                $"{Pct(seg.End)}% {{ transform: translateX({Num(seg.Width)}px); opacity: 1; }} " +
                $"{Pct(seg.End + 0.4)}%, 100% {{ opacity: 0; }} }}\n");
        }

        // The indexer tooltip, between finishing the H1 line and starting the P line.
        sb.Append("""
            .rc-hint { animation: rc-hint 16s infinite; }
            @keyframes rc-hint { 0%, 36% { opacity: 0; } 37.5%, 41.5% { opacity: 1; } 42.5%, 100% { opacity: 0; } }

            .rc-pop { animation: rc-pop 16s infinite; }
            @keyframes rc-pop {
              0%, 53.5% { opacity: 0; transform: translateY(-6px); }
              55%, 70% { opacity: 1; transform: none; }
              71.5%, 100% { opacity: 0; transform: translateY(-6px); }
            }

            .rc-hl { animation: rc-hl 16s infinite; }
            @keyframes rc-hl {
              0%, 54.5% { opacity: 0; transform: translateY(0); }
              56%, 59% { opacity: 1; transform: translateY(0); }
              61%, 63% { opacity: 1; transform: translateY(28px); }
              65%, 70% { opacity: 1; transform: translateY(56px); }
              71.5%, 100% { opacity: 0; transform: translateY(56px); }
            }

            .rc-doc0 { animation: rc-doc0 16s infinite; }
            @keyframes rc-doc0 { 0%, 55% { opacity: 0; } 56.5%, 59% { opacity: 1; } 60.5%, 100% { opacity: 0; } }
            .rc-doc1 { animation: rc-doc1 16s infinite; }
            @keyframes rc-doc1 { 0%, 60.5% { opacity: 0; } 61.5%, 63% { opacity: 1; } 64.5%, 100% { opacity: 0; } }
            .rc-doc2 { animation: rc-doc2 16s infinite; }
            @keyframes rc-doc2 { 0%, 64.5% { opacity: 0; } 65.5%, 70% { opacity: 1; } 71.5%, 100% { opacity: 0; } }

            .rc-end { animation: rc-end 16s infinite; }
            @keyframes rc-end { 0%, 81% { opacity: 0; } 84%, 100% { opacity: 1; } }

            @media (prefers-reduced-motion: reduce) {
              .rc-cv, .rc-ct, .rc-hint, .rc-pop, .rc-hl, .rc-doc, .rc-end { animation: none; }
            }
            """);

        return sb.ToString();
    }
}
