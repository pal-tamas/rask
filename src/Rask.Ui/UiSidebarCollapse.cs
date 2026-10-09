namespace Rask;

/// <summary>Flux's <c>flux:sidebar.collapse</c>: narrows a docked <see cref="UiSidebar" /> to its rail, and closes one slid over the page.</summary>
/// <remarks>
/// Two labels in one control, because the two states are two checkboxes: below the sidebar's breakpoint it
/// is for the one that slid the sidebar in, from it up for the one that narrows it.
/// </remarks>
public sealed partial class UiSidebarCollapse : Component
{
    private const string Root =
        "flex h-8 shrink-0 items-center justify-center "
        + "sidebar-rail:absolute sidebar-rail:opacity-0 sidebar-rail:group-hover/sidebar:opacity-100";

    private const string Pressable =
        "relative flex size-10 cursor-default items-center justify-center gap-2 rounded-lg text-center text-sm font-medium "
        + "whitespace-nowrap text-zinc-500 hover:bg-zinc-800/5 hover:text-zinc-800 "
        + "dark:text-zinc-400 dark:hover:bg-white/15 dark:hover:text-white";

    /// <summary>Pulls the control into its container's padding on that side. Flux's <c>inset</c>.</summary>
    public Ui.Position? Inset { get; set; }

    /// <summary>What the control is called. "Toggle sidebar" unless this says otherwise.</summary>
    public string? Tooltip { get; set; }

    /// <summary>Classes for the control.</summary>
    public string? Class { get; set; }

    internal static string InsetClass(Ui.Position? inset) => inset switch
    {
        Ui.Position.Left => "-ms-2.5",
        Ui.Position.Right => "-me-2.5",
        Ui.Position.Top => "-mt-2.5",
        Ui.Position.Bottom => "-mb-2.5",
        _ => "",
    };

    /// <inheritdoc />
    protected override Component? Render()
    {
        var name = Tooltip ?? "Toggle sidebar";
        return Div.Class(UiClass.Compose(Root, InsetClass(Inset), Class)).Attributes(("data-ui-sidebar-collapse", ""))[
            // Flux's button sits in its tooltip, which is its name. Two here, as the control is two labels:
            // each is shown at the widths its checkbox means something.
            Press(UiSidebarState.Open, name, "flex sidebar-desktop:hidden", ""),
            Press(UiSidebarState.Rail, name, "hidden sidebar-desktop:flex", "sidebar-rail:cursor-e-resize")
        ];
    }

    private static Component Press(string checkbox, string name, string shown, string cursor) =>
        Ui.Tooltip.Content(name).Position(Ui.TooltipPosition.Right).Class(shown)[
            RaskMarkup.Label.For(checkbox).Class(UiClass.Compose(Pressable, cursor)).Role("button").TabIndex(0)[Glyph()]
        ];

    // A panel with its side column marked: drawn here, because Heroicons has no such glyph.
    private static Component Glyph() =>
        Svg.Class("size-5").Fill("none").ViewBox("0 0 20 20").Attributes(("aria-hidden", "true"))[
            SvgPath.Stroke("currentColor").StrokeWidth("1.25").D("M4.75 3.75h10.5a2 2 0 0 1 2 2v8.5a2 2 0 0 1-2 2H4.75a2 2 0 0 1-2-2v-8.5a2 2 0 0 1 2-2ZM7.5 3.75v12.5")
        ];
}
