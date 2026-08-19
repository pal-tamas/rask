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
// the text's right edge, with `steps(n)` where n is the segment's character count — so the reveal lands
// exactly on character boundaries. It is scaleX rather than a translate because a translated cover would
// slide right and eat the NEXT segment on the same line: `Button.` and the rest of its line are two
// segments sharing a baseline, and the popup opens between them.
//
// The cover runs `Bleed` past the text on the right, and its origin is the text's right edge rather than
// its own (`transform-origin: {Width}px`, not `100%`). Both halves matter. A glyph's ink can reach the
// very edge of its advance box — the `>` of `=>` does — so a cover that stops exactly at textLength
// leaves an antialiased sliver of the last character showing from the first frame, which is the "hanging
// character" at the end of an untyped line. Scaling about the TEXT's right edge instead of the rect's
// keeps the reveal schedule identical while the overhang shrinks with the cover, so it is still worth
// `Bleed` at the last step and exactly nothing at scaleX(0) — no residue to blank out the segment that
// follows on the same line.
//
// Every element's BASE state is its FINAL frame — code fully typed, hints dismissed — so
// `prefers-reduced-motion: reduce` can simply turn the animations off and land on the finished component
// rather than on a blank window.
internal sealed partial class ChainAnimation : Component
{
    private const double Loop = 20;    // seconds; one full type-through
    private const double Adv = 10.8;   // monospace advance at font-size 18 (0.6em)
    private const double LineH = 28;
    private const double CodeX = 44;
    private const double Top = 86;     // baseline of line 0
    private const double CanvasW = 880;
    private const double CanvasH = 620;

    // How far the cover rectangle overhangs the text on its right at the tightest step, in px. Two is
    // enough to swallow the antialiased edge of a glyph that fills its advance box; see the note above.
    private const double Bleed = 2;

    // Where the member list hangs from: the caret after `        Button.`, 15 characters in.
    private const double PopupX = CodeX + (15 * Adv);
    private const double PopupY = 352;
    private const double PopupW = 460;
    private const double PopupH = 240;
    private const double RowH = 32;
    private const double RowsY = 360;
    private const double DividerY = 500.5;
    private const double DocHeadY = 522;
    private const double DocY = 540;
    private const double DocLineH = 18;

    private enum Tk
    {
        Plain,
        Kw,
        Ent,
        Mem,
        Str,
        Mut,
        Op,
        Interp,
    }

    private readonly record struct Tok(string Text, Tk Kind);

    private static Tok Kw(string t) => new(t, Tk.Kw);

    private static Tok En(string t) => new(t, Tk.Ent);

    private static Tok Me(string t) => new(t, Tk.Mem);

    private static Tok St(string t) => new(t, Tk.Str);

    private static Tok Mu(string t) => new(t, Tk.Mut);

    private static Tok Pl(string t) => new(t, Tk.Plain);

    /// <summary>An operator: <c>=&gt;</c> is code, not the chrome <see cref="Mu" /> paints.</summary>
    private static Tok Op(string t) => new(t, Tk.Op);

    /// <summary>An interpolation hole: <c>{_count}</c> is code that happens to sit inside a literal.</summary>
    private static Tok In(string t) => new(t, Tk.Interp);

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
        new(0, 0, 1.5, 5.7, [
            Mu("["), En("Route"), Mu("("), St("\"/counter\""), Mu(")]")
        ]),
        new(1, 0, 6.2, 16.6, [
            Kw("public"), Pl(" "), Kw("sealed"), Pl(" "), Kw("partial"), Pl(" "), Kw("class"), Pl(" "),
            Pl("Counter"), Mu(" : "), Pl("Component")
        ]),
        new(2, 0, 17.1, 17.4, [Mu("{")]),
        new(3, 4, 17.8, 22, [
            Kw("private"), Pl(" "), Kw("int"), Pl(" "), Pl("_count"), Mu(";")
        ]),
        new(5, 4, 22.5, 30, [
            Kw("protected"), Pl(" "), Kw("override"), Pl(" "), Pl("Component?"), Pl(" "),
            Me("Render"), Mu("()"), Pl(" "), Op("=>")
        ]),
        new(6, 4, 30, 31, [Mu("[")]),
        new(7, 8, 31.5, 35.5, [
            En("H1"), Mu("["), St("\"Counter\""), Mu("]"), Mu(",")
        ]),
        new(8, 8, 42.5, 49, [
            En("P"), Mu("["), St("$\"Current count: "), In("{_count}"), St("\""), Mu("]"), Mu(",")
        ]),
        // The caret stops here — this is where the member list opens.
        new(9, 8, 49.5, 53, [En("Button"), Mu(".")]),
        new(9, 15, 71.5, 77.5, [
            Me("OnClick"), Mu("(()"), Pl(" "), Op("=>"), Pl(" "), Pl("_count"), Mu("++)"), Mu("["),
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
                "An editor types a Rask Counter page character by character, starting with its "
                + "[Route(\"/counter\")] attribute. As H1[ is written a tooltip "
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
            .ViewBox($"0 0 {Num(CanvasW)} {Num(CanvasH)}")
            .Xmlns("http://www.w3.org/2000/svg")
            .Class("rc-svg")
            .Role("img")[children];
    }

    // ---- the editor window ----
    private static Component Window() =>
        G[
            Rect.X("0.5").Y("0.5").Width(Num(CanvasW - 1)).Height(Num(CanvasH - 1)).Rx("16").Class("rc-win"),
            Rect.X("0.5").Y("0.5").Width(Num(CanvasW - 1)).Height("44").Rx("16").Class("rc-bar"),
            // The title bar's rounded rect would round its bottom corners too; a square patch over the
            // lower half squares them off again without a clip path (and so without an id to collide).
            Rect.X("0.5").Y("28").Width(Num(CanvasW - 1)).Height("17").Class("rc-bar"),
            Line.X1("0").Y1("44.5").X2(Num(CanvasW)).Y2("44.5").Class("rc-rule"),
            Rect.X("0.5").Y("0.5").Width(Num(CanvasW - 1)).Height(Num(CanvasH - 1)).Rx("16").Class("rc-edge"),
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

        // spacingAndGlyphs, not spacing: `spacing` adjusts only the gaps BETWEEN glyphs, so the last
        // glyph keeps its natural advance and its ink can spill past textLength. The cover rectangle is
        // exactly textLength wide, so that spill is never covered — the tail of a line (`=>` on the
        // Render line) hangs there in plain sight from the first frame, before the line is typed.
        // Scaling the glyphs too makes the run occupy exactly the declared width, which is also what
        // keeps the steps() reveal landing on real character boundaries under a fallback font.
        var text = SvgText
            .X(x)
            .Y(baseline)
            .TextLength(Num(seg.Width))
            .LengthAdjust("spacingAndGlyphs")
            .Class("rc-t")[seg.Toks.Select(Span).ToList()];

        return G.Key($"seg{index.ToString(CultureInfo.InvariantCulture)}")[
            text,
            // Painted the window colour and scaled away to the right, so the text appears left-to-right.
            // Wider than the text by `Bleed` per character: the per-segment transform-origin sits on the
            // text's right edge, so that surplus is what keeps a full-height overhang over the last,
            // still-covered glyph at every step, and collapses to nothing when the segment is finished.
            Rect
                .X(x)
                .Y(Num(seg.Baseline - 20))
                .Width(Num(seg.Width + (seg.Chars * Bleed)))
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
        Tk.Op => "rc-op",
        Tk.Interp => "rc-interp",
        _ => "rc-t",
    };

    // ---- the payoff, once the component is written ----
    private static Component Closing() =>
        G.Class("rc-end")[
            Line.X1("44").Y1("508.5").X2("836").Y2("508.5").Class("rc-rule"),
            SvgText.X("44").Y("540").Class("rc-t rc-sm rc-soft")[
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
            Rect.X(Num(PopupX)).Y(Num(PopupY)).Width(Num(PopupW)).Height(Num(PopupH)).Rx("10").Class("rc-bar"),
            Rect.X(Num(PopupX)).Y(Num(PopupY)).Width(Num(PopupW)).Height(Num(PopupH)).Rx("10").Class("rc-edge"),
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
                .X(Num(PopupX + 12))
                .Y(Num(rowY + ((RowH - 12) / 2)))
                .Width("12")
                .Height("12")
                .Rx("3")
                .Class("rc-chip"));
            // Name and type share a baseline even though they are two sizes: it is one list row, and an
            // IDE aligns the two on the text baseline rather than centring each in its own box.
            children.Add(SvgText
                .Key($"name{name}")
                .X(Num(PopupX + 36))
                .Y(Num(rowY + 22))
                .Class("rc-t rc-md")[name]);
            children.Add(SvgText
                .Key($"type{name}")
                .X(Num(PopupX + PopupW - 16))
                .Y(Num(rowY + 22))
                .TextAnchor("end")
                .Class("rc-t rc-xs rc-mut")[type]);
        }

        children.Add(Line
            .X1(Num(PopupX + 12))
            .Y1(Num(DividerY))
            .X2(Num(PopupX + PopupW - 12))
            .Y2(Num(DividerY))
            .Class("rc-rule"));

        // One doc block per member; only the highlighted one is visible at a time.
        for (var i = 0; i < 3; i++)
        {
            var (_, _, doc) = Members[i];
            var block = new List<Component>
            {
                SvgText.X(Num(PopupX + 16)).Y(Num(DocHeadY)).Class("rc-t rc-xs rc-mut")["/// <summary>"],
            };
            for (var l = 0; l < doc.Length; l++)
            {
                block.Add(SvgText
                    .Key($"doc{i.ToString(CultureInfo.InvariantCulture)}-{l.ToString(CultureInfo.InvariantCulture)}")
                    .X(Num(PopupX + 16))
                    .Y(Num(DocY + (l * DocLineH)))
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
            .rc-md { font-size: 17px; }
            .rc-sm { font-size: 15px; }
            .rc-xs { font-size: 14px; }
            .rc-xxs { font-size: 13px; }
            .rc-mut { fill: var(--muted, #8a8aa4); }
            .rc-soft { fill: var(--ink-soft, #c3c2d6); }
            .rc-kw { fill: var(--accent-ink, #c4b5fd); }
            .rc-ent { fill: var(--accent, #8b5cf6); }
            .rc-mem { fill: var(--ink, #e9e9f2); }
            .rc-str { fill: var(--signal, #34d399); }
            .rc-op { fill: var(--ink-soft, #c3c2d6); }
            .rc-interp { fill: var(--accent-ink, #c4b5fd); }
            .rc-chip { fill: var(--accent, #8b5cf6); opacity: 0.35; }
            /* fill-opacity, not opacity: .rc-hl on the same element has its opacity driven by keyframes,
               and two opacity declarations would let the animation paint a solid block over the row. */
            .rc-hlbar { fill: var(--accent, #8b5cf6); fill-opacity: 0.18; }
            /* transform-origin is per segment — the text's right edge, not the cover's. See the note on
               the cover's Bleed at the top of the file. */
            .rc-cv { transform-box: fill-box; }
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

            sb.Append(CultureInfo.InvariantCulture, $".rc-cv{id} {{ transform-origin: {Num(seg.Width)}px 50%; animation: rc-cv{id} {loop}s steps({n}, end) infinite; }}\n");
            sb.Append(CultureInfo.InvariantCulture, $"@keyframes rc-cv{id} {{ 0%, {Pct(seg.Start)}% {{ transform: scaleX(1); }} {Pct(seg.End)}%, 100% {{ transform: scaleX(0); }} }}\n");

            sb.Append(CultureInfo.InvariantCulture, $".rc-ct{id} {{ animation: rc-ct{id} {loop}s steps({n}, end) infinite; }}\n");
            sb.Append(CultureInfo.InvariantCulture,
                $"@keyframes rc-ct{id} {{ 0%, {Pct(seg.Start)}% {{ transform: translateX(0); opacity: 0; }} " +
                $"{Pct(seg.Start + 0.2)}% {{ opacity: 1; }} " +
                $"{Pct(seg.End)}% {{ transform: translateX({Num(seg.Width)}px); opacity: 1; }} " +
                $"{Pct(seg.End + 0.4)}%, 100% {{ opacity: 0; }} }}\n");
        }

        // The indexer tooltip, between finishing the H1 line and starting the P line. Every offset here is
        // a percentage of the loop, so the whole sequence follows Loop without a second edit; only the
        // durations and the highlight's row step are interpolated.
        sb.Append(CultureInfo.InvariantCulture, $$"""
            .rc-hint { animation: rc-hint {{Num(Loop)}}s infinite; }
            @keyframes rc-hint { 0%, 36% { opacity: 0; } 37.5%, 41.5% { opacity: 1; } 42.5%, 100% { opacity: 0; } }

            .rc-pop { animation: rc-pop {{Num(Loop)}}s infinite; }
            @keyframes rc-pop {
              0%, 53.5% { opacity: 0; transform: translateY(-6px); }
              55%, 70% { opacity: 1; transform: none; }
              71.5%, 100% { opacity: 0; transform: translateY(-6px); }
            }

            .rc-hl { animation: rc-hl {{Num(Loop)}}s infinite; }
            @keyframes rc-hl {
              0%, 54.5% { opacity: 0; transform: translateY(0); }
              56%, 59% { opacity: 1; transform: translateY(0); }
              61%, 63% { opacity: 1; transform: translateY({{Num(RowH)}}px); }
              65%, 70% { opacity: 1; transform: translateY({{Num(RowH * 2)}}px); }
              71.5%, 100% { opacity: 0; transform: translateY({{Num(RowH * 2)}}px); }
            }

            .rc-doc0 { animation: rc-doc0 {{Num(Loop)}}s infinite; }
            @keyframes rc-doc0 { 0%, 55% { opacity: 0; } 56.5%, 59% { opacity: 1; } 60.5%, 100% { opacity: 0; } }
            .rc-doc1 { animation: rc-doc1 {{Num(Loop)}}s infinite; }
            @keyframes rc-doc1 { 0%, 60.5% { opacity: 0; } 61.5%, 63% { opacity: 1; } 64.5%, 100% { opacity: 0; } }
            .rc-doc2 { animation: rc-doc2 {{Num(Loop)}}s infinite; }
            @keyframes rc-doc2 { 0%, 64.5% { opacity: 0; } 65.5%, 70% { opacity: 1; } 71.5%, 100% { opacity: 0; } }

            .rc-end { animation: rc-end {{Num(Loop)}}s infinite; }
            @keyframes rc-end { 0%, 81% { opacity: 0; } 84%, 100% { opacity: 1; } }

            @media (prefers-reduced-motion: reduce) {
              .rc-cv, .rc-ct, .rc-hint, .rc-pop, .rc-hl, .rc-doc, .rc-end { animation: none; }
            }
            """);

        return sb.ToString();
    }
}
