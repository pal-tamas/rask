namespace Rask;

/// <summary>
/// An application sidebar beside the page: docked from a breakpoint up, sliding over the page below it.
/// </summary>
/// <remarks>
/// <para>
/// Flux UI's sidebar layout, on daisyUI's drawer. The children are the SIDEBAR — a brand, a <see cref="UiNavList" />,
/// a <see cref="UiSpacer" /> and a profile at the bottom — and <see cref="Page" /> is everything beside it, usually
/// the router outlet. It renders an <c>&lt;aside&gt;</c> landmark, sticky and full-height while docked, so a long page
/// scrolls under a sidebar that stays put.
/// </para>
/// <para>
/// Below <see cref="Collapsible" /> the sidebar is off-screen and a <see cref="UiSidebarToggle" /> slides it in; a
/// click on the page beside it slides it back. That open state lives in daisyUI's checkbox, so it works on a
/// prerendered page with no runtime at all. <see cref="Open" /> and <see cref="OnToggle" /> hand it to C# as well —
/// what lets a page close it when a navigation completes.
/// </para>
/// </remarks>
public sealed partial class UiSidebar : Component
{
    /// <summary>Joins the sidebar to its <see cref="UiSidebarToggle" />. Must be unique on the page.</summary>
    public required string Id { get; set; }

    /// <summary>The page beside the sidebar — typically the router's outlet.</summary>
    public required Component Page { get; set; }

    /// <summary>
    ///     The width below which the sidebar slides over the page instead of sitting beside it. Unset, it is always
    ///     beside it.
    /// </summary>
    public Ui.Breakpoint? Collapsible { get; set; }

    /// <summary>Which edge the sidebar is on: <see cref="Ui.Position.Left" /> by default, or <see cref="Ui.Position.Right" />.</summary>
    public Ui.Position? Position { get; set; }

    /// <summary>Whether the sidebar is slid in, while it is collapsed. Unset leaves the state to the checkbox alone.</summary>
    public bool? Open { get; set; }

    /// <summary>Runs when the sidebar is slid in or out, with the state being asked for.</summary>
    public Callback<bool> OnToggle { get; set; }

    /// <summary>
    ///     Lets a DOCKED sidebar be narrowed to a rail of icons, with a <see cref="UiSidebarCollapse" /> to do it.
    /// </summary>
    /// <remarks>
    ///     Flux UI's collapsed sidebar, and a different thing from <see cref="Collapsible" />: that one says at what
    ///     width the sidebar stops being beside the page at all. This one keeps it beside the page and takes the
    ///     words away, which is what a dense application wants on a laptop.
    /// </remarks>
    public bool? Collapsable { get; set; }

    /// <summary>
    ///     Whether the docked sidebar is narrowed to its rail. Unset leaves the state to the checkbox alone.
    /// </summary>
    /// <remarks>
    ///     Unset, the reader collapses and expands it and the page is not asked — it works on a prerendered page
    ///     with no runtime, like the drawer. Set it to take ownership, which is what lets a page REMEMBER the
    ///     choice across a full page load; pair it with <see cref="OnCollapse" />.
    /// </remarks>
    public bool? Collapsed { get; set; }

    /// <summary>Runs when the reader narrows or widens the docked sidebar, with the state being asked for.</summary>
    public Callback<bool> OnCollapse { get; set; }

    /// <summary>The name of the sidebar landmark. "Sidebar" unless this says otherwise.</summary>
    public string? AccessibleLabel { get; set; }

    /// <summary>The name of the click-away area that closes it. "Close sidebar" unless this says otherwise.</summary>
    public string? CloseLabel { get; set; }

    /// <summary>Classes for the whole layout — the drawer around the sidebar and the page.</summary>
    public string? Class { get; set; }

    /// <summary>Classes for the sidebar panel itself, the <c>&lt;aside&gt;</c>: its width, its padding, its ground.</summary>
    public string? PanelClass { get; set; }

    // Which breakpoint the rail applies from, as a VALUE rather than a class name — so it lives here rather
    // than in UiClassNames, which holds only complete Tailwind class names the shipped sheet defines. The
    // rail's rules are the kit's own CSS, keyed by this attribute, because no Tailwind variant can say "while
    // this drawer is open in the flow".
    private static string RailFrom(Ui.Breakpoint value) => value switch
    {
        Ui.Breakpoint.Sm => "sm",
        Ui.Breakpoint.Md => "md",
        Ui.Breakpoint.Xl => "xl",
        _ => "lg",
    };

    /// <inheritdoc />
    protected override Component? Render()
    {
        var toggle = Input.Of<bool>().Checked(Open == true).Id(Id).Class("drawer-toggle");
        if (OnToggle.HasValue)
        {
            toggle = toggle.OnChange(OnToggle);
        }

        // The rail's own checkbox, beside the drawer's: a sibling of `.drawer-side`, which is what lets the
        // kit's `:checked ~ .drawer-side` rules narrow the panel with no script. Rendered only when asked for,
        // so a sidebar that cannot collapse carries no stray input.
        Component? rail = null;
        if (Collapsable == true)
        {
            var box = Input.Of<bool>().Checked(Collapsed == true).Id(Id + "-rail").Class("ui-sidebar-rail")
                .Type(InputType.Checkbox);
            rail = OnCollapse.HasValue ? box.OnChange(OnCollapse) : box;
        }

        var root = Div.Class(UiClass.Compose(
            "drawer min-h-dvh",
            Collapsible is { } from ? UiClassNames.SidebarInFlowFrom(from) : "drawer-open",
            Position is { } position ? UiClassNames.DrawerPosition(position) : "",
            Class));

        if (Collapsable == true)
        {
            // The rail applies only where the sidebar is DOCKED, and a Tailwind variant cannot say "while the
            // drawer is open in the flow" — so the breakpoint travels as a value the kit's own media queries key
            // off.
            root = root.Data("ui-rail", RailFrom(Collapsible ?? Ui.Breakpoint.Lg));
        }

        return root[
            toggle,
            rail,
            Div.Class("drawer-content flex min-w-0 flex-col")[Page],
            Div.Class("drawer-side z-40")[
                RaskMarkup.Label
                    .For(Id)
                    .Class("drawer-overlay")
                    .Aria("label", CloseLabel ?? "Close sidebar"),
                Aside
                    .Class(UiClass.Compose(
                        "ui-sidebar-panel flex min-h-full w-64 flex-col gap-4 overflow-hidden border-e "
                        + "border-base-300 bg-base-100 p-4",
                        PanelClass))
                    .Aria("label", AccessibleLabel ?? "Sidebar")[
                    Children ?? []
                ]
            ]
        ];
    }
}
