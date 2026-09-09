namespace Rask.Ui;

/// <summary>A row of link-shaped tabs. Navigation, so each one is a real link with a real URL.</summary>
/// <remarks>
/// <para>
/// daisyUI's <c>tabs</c>, and <b>links rather than a selected index</b> — which is the one place this
/// category deliberately did not move onto C# state. A tab that is a URL is bookmarkable, survives a
/// refresh, answers the back button and works before the runtime boots; a tab that is an index in a
/// field is none of those. Where a page genuinely has no URL for a view, put the state in the page and
/// render the panel yourself.
/// </para>
/// <para>
/// Scrolls rather than wraps: these carry counts that change as a queue drains, and a wrapping row
/// would change height underneath an operator mid-read.
/// </para>
/// </remarks>
public sealed partial class UiTabs : Component
{
    /// <summary>How the row is drawn.</summary>
    public new UiTabStyle? Style { get; set; }

    public UiSize? Size { get; set; }

    /// <summary>
    ///     Which side of its panel the row sits on. Only <see cref="UiPlacement.Top" /> and
    ///     <see cref="UiPlacement.Bottom" /> mean anything here; anything else draws the default.
    /// </summary>
    public UiPlacement? Placement { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        // role=tablist on a <nav> of links: daisyUI's own markup for the link form, and what tells
        // assistive technology these are alternatives rather than an arbitrary run of links.
        Nav.Role("tablist")
            .Class(UiClass.Compose(
                "tabs",
                Style is { } style ? UiClassNames.TabsStyle(style) : "",
                Size is { } size ? UiClassNames.TabsSize(size) : "",
                Placement is { } placement ? UiClassNames.TabsPlacement(placement) : "",
                "-mx-3 flex-nowrap overflow-x-auto px-3 sm:mx-0 sm:flex-wrap sm:px-0 "
                + "[-ms-overflow-style:none] [scrollbar-width:none] [&::-webkit-scrollbar]:hidden",
                Class))[
            Children ?? []
        ];
}
