namespace Rask;

/// <summary>The breadcrumb bar: what you are looking at, and how to get to a sibling of it.</summary>
public sealed partial class UiTopBar : Component
{
    /// <summary>Pushed to the trailing edge — links, never state an operator has to act on.</summary>
    public Component? Trailing { get; set; }

    /// <summary>
    ///     Keeps the bar at the top of the viewport while the page scrolls under it.
    /// </summary>
    /// <remarks>
    ///     Flux UI's <c>header sticky</c>. It needs an opaque ground, which the bar already has, and a stacking
    ///     context above the content, which is what the <c>z-30</c> is for — below <see cref="UiSidebar" />'s
    ///     drawer, so a sidebar sliding in still covers it.
    /// </remarks>
    public bool? Sticky { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Header.Class(UiClass.Compose(
            "flex items-center gap-1 border-b border-base-300 px-2 py-2 sm:gap-2 sm:px-4 sm:py-2.5",
            Sticky == true ? "sticky top-0 z-30 bg-base-100" : "",
            Class))[
            Children ?? [],
            Trailing is null ? null : Div.Class("ml-auto flex items-center gap-1 pl-2 sm:gap-3")[Trailing]
        ];
}
