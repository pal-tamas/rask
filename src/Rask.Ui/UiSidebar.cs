namespace Rask;

/// <summary>Flux's <c>flux:sidebar</c>: the application's navigation, beside the page.</summary>
/// <remarks>
/// <para>
/// A sibling of <see cref="UiHeader" /> and <see cref="UiMain" />: whatever holds a <see cref="UiMain" /> —
/// the body, or a wrapper — is the layout grid, and the sidebar takes its first column. It paints nothing
/// of its own; the ground and the border are the call site's classes, as in Flux.
/// </para>
/// <para>
/// Both states are checkboxes, so they work with no script: slid over the page below
/// <see cref="Breakpoint" />, narrowed to a rail of icons from it up. Neither is a prop, as neither is in
/// Flux. The runtime does what Flux's script does with them: the overlay
/// is put away when the app navigates (<c>data-rask-uncheck-on-navigate</c>), and the rail is remembered across
/// visits (<c>data-rask-persist</c>, under Flux's own storage key — <see cref="UiSidebarScript" /> restores it
/// before first paint in a WebAssembly app).
/// </para>
/// </remarks>
public sealed partial class UiSidebar : Component
{
    private const string Base = "group/sidebar z-20 flex flex-col gap-4 p-4 [grid-area:sidebar] [:where(&)]:w-64";

    // Below the breakpoint it is over the page and off-screen until opened; from it up it is in the grid.
    private const string PutAway =
        "fixed inset-y-0 start-0 max-h-dvh min-h-dvh overflow-y-auto overscroll-contain transition-transform "
        + "-translate-x-full rtl:translate-x-full sidebar-open:translate-x-0 "
        + "sidebar-desktop:min-h-[auto] sidebar-desktop:translate-x-0";

    private const string Rail = "sidebar-rail:w-14 sidebar-rail:cursor-e-resize sidebar-rail:px-2";

    /// <summary>Keeps the docked sidebar in view while the page scrolls, scrolling inside itself when it is taller.</summary>
    public bool? Sticky { get; set; }

    /// <summary>When the sidebar can be put away. Never, unless this says otherwise.</summary>
    public Ui.SidebarCollapsible? Collapsible { get; set; }

    /// <summary>Flux's deprecated spelling of <see cref="Ui.SidebarCollapsible.Mobile" />.</summary>
    public bool? Stashable { get; set; }

    /// <summary>The width the sidebar docks from. <see cref="Ui.Breakpoint.Lg" /> (1024px) unless this says otherwise.</summary>
    public Ui.Breakpoint? Breakpoint { get; set; }

    /// <summary>
    ///     Whether the collapsed rail is remembered across visits. On unless this is <see langword="false" />.
    /// </summary>
    public bool? Persist { get; set; }

    /// <summary>Classes for the sidebar: its ground, its border, another width.</summary>
    public string? Class { get; set; }

    private static string Docked(bool sticky, bool putAway) => (sticky, putAway) switch
    {
        (true, true) => "sidebar-desktop:sticky sidebar-desktop:top-0",
        (true, false) => "sticky top-0 max-h-dvh overflow-y-auto overscroll-contain",
        (false, true) => "sidebar-desktop:static sidebar-desktop:max-h-none sidebar-desktop:overflow-visible",
        _ => "",
    };

    private static string Width(Ui.Breakpoint value) => value switch
    {
        Ui.Breakpoint.Sm => "sm",
        Ui.Breakpoint.Md => "md",
        Ui.Breakpoint.Xl => "xl",
        _ => "lg",
    };

    // Unset when the sidebar was told not to remember its rail: the hook is then not asked for.
    private string? RailKey => Persist == false ? null : UiSidebarState.RailKey;

    /// <inheritdoc />
    protected override Component? Render()
    {
        var collapsible = Collapsible ?? (Stashable == true ? Ui.SidebarCollapsible.Mobile : Ui.SidebarCollapsible.Never);
        var putAway = collapsible != Ui.SidebarCollapsible.Never;
        var rails = collapsible == Ui.SidebarCollapsible.Always;

        var sidebar = Div
            .Class(UiClass.Compose(
                Base,
                putAway ? PutAway : "",
                Docked(Sticky == true, putAway),
                rails ? Rail : "",
                Class))
            .Attributes(UiMarks.Present(
                ("data-ui-sidebar", ""),
                ("data-breakpoint", putAway ? Width(Breakpoint ?? Ui.Breakpoint.Lg) : null)))[
            // Slid over the page: put away again when the app goes to another page, as Flux's is.
            putAway ? State(UiSidebarState.Open, "data-ui-sidebar-open", ("data-rask-uncheck-on-navigate", "")) : null,
            // Narrowed to its rail: kept across visits under the key Flux's own script keeps it under.
            rails ? State(UiSidebarState.Rail, "data-ui-sidebar-rail", ("data-rask-persist", RailKey)) : null,
            // A click anywhere on the rail widens it again, as in Flux: a label for the same checkbox, under
            // everything else in the sidebar.
            rails
                ? RaskMarkup.Label
                    .For(UiSidebarState.Rail)
                    .Class("absolute inset-0 -z-10 hidden cursor-e-resize sidebar-rail:block")
                    .Attributes(("data-ui-mechanism", ""), ("aria-hidden", "true"))
                : null,
            Children ?? []
        ];

        if (!putAway)
        {
            return sidebar;
        }

        return
        [
            RaskMarkup.Label
                .For(UiSidebarState.Open)
                .Class("fixed inset-0 z-20 cursor-auto bg-black/10")
                .Attributes(("data-ui-sidebar-backdrop", "")),
            sidebar
        ];
    }

    // A hook with no value is one the sidebar was told not to ask for (`Persist(false)`).
    private static HTMLInputElement<bool> State(string id, string marker, (string Name, string? Value) hook) =>
        Input.Of<bool>().Id(id).Type(InputType.Checkbox).Class("hidden")
            .Attributes(hook.Value is null ? [(marker, "")] : [(marker, ""), hook]);
}
