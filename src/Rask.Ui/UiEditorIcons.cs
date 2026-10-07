using static Rask.Markup;

namespace Rask;

/// <summary>
///     The editor's pictures that Heroicons has no drawing of: Flux draws these from Lucide, and so does the kit.
/// </summary>
/// <remarks>
///     Path data from Lucide 0.300.0 (https://lucide.dev, ISC License, Copyright (c) Lucide Contributors),
///     rescaled from its 24 grid to the 20 grid Flux draws a toolbar icon on at a 1.5 stroke; undo and redo keep
///     the 24 grid, as Flux's do. The release was chosen by the measured boxes of Flux's own icons.
/// </remarks>
internal static class UiEditorIcons
{
    // Lucide `type`.
    private static readonly string[] ParagraphShapes =
    [
        "M3.333 5.833L3.333 3.333 16.667 3.333 16.667 5.833",
        "M7.5 16.667L12.5 16.667",
        "M10 3.333L10 16.667",
    ];

    // Lucide `list-ordered`.
    private static readonly string[] OrderedShapes =
    [
        "M8.333 10L17.5 10",
        "M8.333 15L17.5 15",
        "M8.333 5L17.5 5",
        "M3.333 8.333h1.667",
        "M3.333 5h0.833v3.333",
        "M5 15H3.333c0 -0.833 1.667 -1.667 1.667 -2.5s-0.833 -1.25 -1.667 -0.833",
    ];

    // Lucide `text-quote`.
    private static readonly string[] BlockquoteShapes =
    [
        "M14.167 5H2.5",
        "M17.5 10H6.667",
        "M17.5 15H6.667",
        "M2.5 10v5",
    ];

    // Lucide `link-2`.
    private static readonly string[] LinkShapes =
    [
        "M7.5 14.167H5.833A4.167 4.167 0 0 1 5.833 5.833h1.667",
        "M12.5 5.833h1.667a4.167 4.167 0 1 1 0 8.333h-1.667",
        "M6.667 10L13.333 10",
    ];

    // Lucide `unlink`.
    private static readonly string[] UnlinkShapes =
    [
        "m15.7 10.208 1.433 -1.425h-0.017a4.17 4.17 0 0 0 -0.1 -5.892 4.172 4.172 0 0 0 -5.792 0l-1.433 1.425",
        "m4.308 9.792 -1.425 1.425a4.17 4.17 0 0 0 0.1 5.892 4.172 4.172 0 0 0 5.792 0l1.425 -1.425",
        "M6.667 1.667L6.667 4.167",
        "M1.667 6.667L4.167 6.667",
        "M13.333 15.833L13.333 18.333",
        "M15.833 13.333L18.333 13.333",
    ];

    // Lucide `undo`.
    private static readonly string[] UndoShapes =
    [
        "M3 7v6h6",
        "M21 17a9 9 0 0 0-9-9 9 9 0 0 0-6 2.3L3 13",
    ];

    // Lucide `redo`.
    private static readonly string[] RedoShapes =
    [
        "M21 7v6h-6",
        "M3 17a9 9 0 0 1 9-9 9 9 0 0 1 6 2.3l3 2.7",
    ];

    // Lucide `subscript`.
    private static readonly string[] SubscriptShapes =
    [
        "m3.333 4.167 6.667 6.667",
        "m10 4.167 -6.667 6.667",
        "M16.667 15.833h-3.333c0 -1.25 0.367 -1.667 1.25 -2.083S16.667 12.775 16.667 11.667c0 -0.392 -0.142 -0.775 -0.4 -1.075a1.758 1.758 0 0 0 -2.183 -0.367c-0.35 0.2 -0.617 0.517 -0.75 0.892",
    ];

    // Lucide `superscript`.
    private static readonly string[] SuperscriptShapes =
    [
        "m3.333 15.833 6.667 -6.667",
        "m10 15.833 -6.667 -6.667",
        "M16.667 10h-3.333c0 -1.25 0.368 -1.667 1.25 -2.083S16.667 6.945 16.667 5.835c0 -0.393 -0.142 -0.775 -0.403 -1.075a1.754 1.754 0 0 0 -2.181 -0.363c-0.35 0.199 -0.615 0.512 -0.749 0.883",
    ];

    // Lucide `highlighter`.
    private static readonly string[] HighlightShapes =
    [
        "m7.5 9.167 -5 5v2.5h7.5l2.5 -2.5",
        "m18.333 10 -3.833 3.833a1.667 1.667 0 0 1 -2.333 0l-4.333 -4.333a1.667 1.667 0 0 1 0 -2.333L11.667 3.333",
    ];

    // Lucide `code`.
    private static readonly string[] CodeShapes =
    [
        "M13.333 15L18.333 10 13.333 5",
        "M6.667 5L1.667 10 6.667 15",
    ];

    /// <summary>
    ///     A Heroicon as Flux writes one straight into the toolbar: not through <c>flux:icon</c>, so it has no
    ///     marker and no <c>shrink-0</c> — only its size.
    /// </summary>
    internal static Component Hero(Ui.IconName name, Ui.IconVariant variant = Ui.IconVariant.Mini, string? extra = null) =>
        UiIcon.Slot(name, variant, UiClass.Compose(variant == Ui.IconVariant.Micro ? "size-4" : "size-5", extra));

    internal static Component Paragraph() => Stroked(ParagraphShapes);

    internal static Component Ordered() => Stroked(OrderedShapes);

    internal static Component Blockquote() => Stroked(BlockquoteShapes);

    internal static Component Link() => Stroked(LinkShapes);

    internal static Component Subscript() => Stroked(SubscriptShapes);

    internal static Component Superscript() => Stroked(SuperscriptShapes);

    internal static Component Highlight() => Stroked(HighlightShapes);

    internal static Component Code() => Stroked(CodeShapes);

    internal static Component Undo() => Wide(UndoShapes);

    internal static Component Redo() => Wide(RedoShapes);

    /// <summary>
    ///     Flux clips this one to its own box, by a clip path the SVG carries; <paramref name="clipId" /> names it,
    ///     once per page.
    /// </summary>
    internal static Component Unlink(string clipId) =>
        Frame("0 0 20 20").Class("shrink-0")[
            G.Attributes(("clip-path", "url(#" + clipId + ")"))[UnlinkShapes.Select(Stroke)],
            Defs[ClipPath.Id(clipId)[Rect.Attributes(("width", "20"), ("height", "20"), ("fill", "white"))]]
        ];

    private static Component Stroked(string[] shapes) => Frame("0 0 20 20")[shapes.Select(Stroke)];

    private static Component Wide(string[] shapes) =>
        Svg.Class("size-5").Attributes(
            ("viewBox", "0 0 24 24"),
            ("fill", "none"),
            ("stroke", "currentColor"),
            ("stroke-width", "1.8"),
            ("stroke-linecap", "round"),
            ("stroke-linejoin", "round"),
            ("aria-hidden", "true"))[shapes.Select((shape, index) => (Component)SvgPath.Key(index).D(shape))];

    private static SVGSVGElement Frame(string viewBox) =>
        Svg.Attributes(("width", "20"), ("height", "20"), ("viewBox", viewBox), ("fill", "none"), ("aria-hidden", "true"));

    private static Component Stroke(string shape, int index) =>
        SvgPath.Key(index).Attributes(("stroke", "currentColor"), ("stroke-width", "1.5"), ("stroke-linecap", "round"), ("stroke-linejoin", "round")).D(shape);
}
