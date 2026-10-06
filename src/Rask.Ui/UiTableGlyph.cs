namespace Rask;

/// <summary>
///     The two icons a sortable heading draws: Heroicons' <c>chevron-down</c> and <c>chevron-up</c>, in the
///     16px "micro" set (MIT, heroicons 2.2.0 <c>16/solid</c>).
/// </summary>
/// <remarks>
///     Here only until <c>Ui.Icon</c> draws Flux's icon set: then this is
///     <c>Ui.Icon.Name(…).Variant(Micro)</c> and the file goes.
/// </remarks>
internal static class UiTableGlyph
{
    private const string Down =
        "M4.22 6.22a.75.75 0 0 1 1.06 0L8 8.94l2.72-2.72a.75.75 0 1 1 1.06 1.06l-3.25 3.25a.75.75 0 0 1-1.06 0L4.22 7.28a.75.75 0 0 1 0-1.06Z";

    private const string Up =
        "M11.78 9.78a.75.75 0 0 1-1.06 0L8 7.06 5.28 9.78a.75.75 0 0 1-1.06-1.06l3.25-3.25a.75.75 0 0 1 1.06 0l3.25 3.25a.75.75 0 0 1 0 1.06Z";

    internal static Component Chevron(bool down) =>
        Markup.Svg.ViewBox("0 0 16 16")
            .Fill("currentColor")
            .Class("size-4 shrink-0")
            .Data("ui-icon", "")
            .Attributes(("xmlns", "http://www.w3.org/2000/svg"), ("aria-hidden", "true"), ("data-slot", "icon"))[
            Markup.SvgPath.D(down ? Down : Up).Attributes(("fill-rule", "evenodd"), ("clip-rule", "evenodd"))
        ];
}
