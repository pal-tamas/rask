namespace Rask;

/// <summary>Flux's <c>flux:sidebar.toggle</c>: the button that slides a <see cref="UiSidebar" /> over the page — the hamburger in a phone's header.</summary>
/// <remarks>
/// A <c>&lt;label&gt;</c> for the sidebar's checkbox, with a button's role and a tab stop; the runtime presses
/// it on Enter and Space. From the sidebar's breakpoint up there is nothing to slide, and it is not shown.
/// </remarks>
public sealed partial class UiSidebarToggle : Component
{
    private const string Root =
        "relative flex size-10 shrink-0 cursor-default items-center justify-center gap-2 rounded-lg text-center text-sm "
        + "font-medium whitespace-nowrap text-zinc-500 hover:bg-zinc-800/5 hover:text-zinc-800 "
        + "dark:text-zinc-400 dark:hover:bg-white/15 dark:hover:text-white";

    /// <summary>The icon drawn. <see cref="Ui.IconName.Bars2" /> unless this says otherwise.</summary>
    public Ui.IconName? Icon { get; set; }

    /// <summary>Pulls the button into its container's padding on that side. Flux's <c>inset</c>.</summary>
    public Ui.Position? Inset { get; set; }

    /// <summary>Classes for the button.</summary>
    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        RaskMarkup.Label
            .For(UiSidebarState.Open)
            .Class(UiClass.Compose(Root, UiSidebarCollapse.InsetClass(Inset), Class))
            .Role("button")
            .TabIndex(0)
            .Attributes(("data-ui-sidebar-toggle", ""))
            .Aria("label", RaskStrings.Get(RaskString.SidebarToggle, "Toggle sidebar"))[
            Ui.Icon.Name(Icon ?? Ui.IconName.Bars2).Mini
        ];
}
