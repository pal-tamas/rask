using Rask.Core.Components;

namespace Rask;

/// <summary>
///     Flux's <c>flux:icon.*</c>: one of <see cref="Ui.IconName" />, drawn as inline SVG in one of four
///     <see cref="Ui.IconVariant" />s.
/// </summary>
/// <remarks>
///     <para>
///     The drawings are <see href="https://heroicons.com">Heroicons</see> (MIT), generated into the assembly by
///     <c>scripts/flux/icons.mjs</c>, so the kit ships no icon font and no static assets.
///     <c>Ui.Icon.Name(Ui.IconName.Bolt)</c> is the 24px outline; <c>.Solid</c>, <c>.Mini</c> and <c>.Micro</c>
///     pick the others.
///     </para>
///     <para>
///     Painted in <c>currentColor</c> and <c>aria-hidden</c>: an icon takes the colour of the text it sits in,
///     and sits beside a label a screen reader already reads.
///     </para>
/// </remarks>
public sealed partial class UiIcon : Component
{
    // Tailwind's own spinner, which is what Flux draws: a faint ring and a darker arc of it.
    private const string SpinnerArc =
        "M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z";

    // One dictionary for every icon drawn. `data-slot` is Heroicons' own mark, which Flux keeps.
    private static readonly Dictionary<string, string?> Marks = new(StringComparer.Ordinal)
    {
        ["data-ui-icon"] = null,
        ["data-slot"] = "icon",
        ["aria-hidden"] = "true",
    };

    /// <summary>Which icon to draw.</summary>
    public required Ui.IconName Name { get; set; }

    /// <summary>Which drawing of it, and so how large. Outline when unset.</summary>
    public Ui.IconVariant? Variant { get; set; }

    /// <summary>
    ///     Classes for the call site, added to the icon's own. A <c>size-*</c> here overrides the variant's
    ///     size and a <c>text-*</c> sets the colour.
    /// </summary>
    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        var variant = Variant ?? Ui.IconVariant.Outline;
        return Draw(Name, variant, Svg.Class(Classes(variant, Name == Ui.IconName.Loading), Class).Attributes(Marks));
    }

    /// <summary>
    ///     The drawing alone, classed by the part that places it: where Flux writes a Heroicon straight into a
    ///     part (a toast's variant glyph) rather than through <c>flux:icon</c>, it carries no size, no
    ///     <c>shrink-0</c> and no attribute of its own.
    /// </summary>
    internal static Component Bare(Ui.IconName name, Ui.IconVariant variant, string classes) =>
        Draw(name, variant, Svg.Class(classes));

    /// <summary><see cref="Bare" />, as Heroicons ship it: <c>aria-hidden</c> and <c>data-slot="icon"</c>.</summary>
    internal static Component Slot(Ui.IconName name, Ui.IconVariant variant, string classes) =>
        Draw(name, variant, Svg.Class(classes).Attributes(("data-slot", "icon"), ("aria-hidden", "true")));

    private static Component Draw(Ui.IconName name, Ui.IconVariant variant, SVGSVGElement icon)
    {
        if (name == Ui.IconName.Loading)
        {
            return icon.Fill("none").ViewBox("0 0 24 24")[
                Circle.Class("opacity-25").Stroke("currentColor").StrokeWidth("4").Cx("12").Cy("12").R("10"),
                SvgPath.Class("opacity-75").Fill("currentColor").D(SpinnerArc)
            ];
        }

        icon = variant switch
        {
            Ui.IconVariant.Outline => icon.Fill("none").Stroke("currentColor").StrokeWidth("1.5").ViewBox("0 0 24 24"),
            Ui.IconVariant.Mini => icon.Fill("currentColor").ViewBox("0 0 20 20"),
            Ui.IconVariant.Micro => icon.Fill("currentColor").ViewBox("0 0 16 16"),
            _ => icon.Fill("currentColor").ViewBox("0 0 24 24"),
        };

        return icon[Shapes(UiIconPaths.For(name, variant), variant == Ui.IconVariant.Outline)];
    }

    // The size sits in :where(), which weighs nothing, so a size-* from the call site wins whatever order
    // the stylesheet lists the two in.
    private static string Classes(Ui.IconVariant variant, bool spins) => (variant, spins) switch
    {
        (Ui.IconVariant.Mini, false) => "shrink-0 [:where(&)]:size-5",
        (Ui.IconVariant.Micro, false) => "shrink-0 [:where(&)]:size-4",
        (_, false) => "shrink-0 [:where(&)]:size-6",
        (Ui.IconVariant.Mini, true) => "shrink-0 [:where(&)]:size-5 animate-spin",
        (Ui.IconVariant.Micro, true) => "shrink-0 [:where(&)]:size-4 animate-spin",
        (_, true) => "shrink-0 [:where(&)]:size-6 animate-spin",
    };

    private static Component[] Shapes(UiIconShape[] shapes, bool stroked)
    {
        var parts = new Component[shapes.Length];
        for (var i = 0; i < shapes.Length; i++)
        {
            parts[i] = Shape(shapes[i], stroked);
        }

        return parts;
    }

    private static Component Shape(UiIconShape shape, bool stroked) => shape.Mark switch
    {
        UiIconShape.EvenOdd => SvgPath.FillRule("evenodd").ClipRule("evenodd").D(shape.Data),
        UiIconShape.RoundJoin => SvgPath.StrokeLinejoin("round").D(shape.Data),
        UiIconShape.Rectangle => Rectangle(shape.Data.Split(' ')),
        _ when stroked => SvgPath.StrokeLinecap("round").StrokeLinejoin("round").D(shape.Data),
        _ => SvgPath.D(shape.Data),
    };

    private static SVGRectElement Rectangle(string[] box) =>
        Rect.X(box[0]).Y(box[1]).Width(box[2]).Height(box[3]).Rx(box[4]);
}
