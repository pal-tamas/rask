namespace Rask.Ui;

/// <summary>
/// <see cref="UiThemePicker" /> behind a trigger, for a bar with no room for thirty-five radios.
/// </summary>
/// <remarks>
/// <para>
/// A <see cref="UiPopover" />, so it closes the way everything else that floats does: on Escape, on a click
/// outside, and on its own trigger, handing focus back to the trigger. It used to be a <c>&lt;details&gt;</c>,
/// which opened with no JavaScript but closed on nothing but its summary — a reader who opened it to look
/// had to find that button again to put it away.
/// </para>
/// <para>
/// It stays open while a theme is picked. The page restyles the moment a radio is checked, so the arrow keys
/// walk the palettes as a live preview; closing on the first pick would end that at the first step.
/// </para>
/// <para>
/// It is not built on <see cref="UiDropdown" />, and the reason is structural rather than stylistic:
/// that component is a MENU — it supplies its own <c>&lt;ul role="menu"&gt;</c> and walks rows with its own
/// cursor, and the picker is already a <c>&lt;ul&gt;</c> of radios the browser walks itself. A popover is a
/// panel of anything, so the picker goes in as it is.
/// </para>
/// <para>
/// The list scrolls. Thirty-five themes is taller than most viewports, and a dropdown running off the
/// bottom of the screen hides exactly the half someone is scrolling to find.
/// </para>
/// </remarks>
public sealed partial class UiThemeDropdown : Component
{
    /// <summary>The label on the trigger. Defaults to "Theme".</summary>
    public string Trigger { get; set; } = "Theme";

    /// <summary>Which side of the trigger the list opens on.</summary>
    public UiPosition? Position { get; set; }

    /// <summary>
    ///     Where along that side the list sits. <see cref="UiAlign.End" /> for a trigger at the end of a bar,
    ///     so the list opens back over the page instead of off its edge.
    /// </summary>
    /// <remarks>
    ///     This used to be a free-form string of daisyUI class names, which is the one shape the kit's class
    ///     rule forbids: a misspelt class compiles, and styles nothing.
    /// </remarks>
    public UiAlign? Align { get; set; }

    /// <summary>The radio group's name, passed through to the picker.</summary>
    public string GroupName { get; set; } = "rask-ui-theme";

    /// <summary>The themes to offer. Defaults to every theme the kit ships.</summary>
    public IReadOnlyList<UiThemeName>? Themes { get; set; }

    /// <inheritdoc cref="UiThemePicker.ShowSystem" />
    public bool ShowSystem { get; set; } = true;

    /// <inheritdoc cref="UiThemePicker.SystemLabel" />
    public string SystemLabel { get; set; } = "System";

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        UiPopover
            .Trigger(Trigger)
            .Icon(UiIconName.Sparkles)
            .Size(UiSize.Sm)
            .Position(Position)
            .Align(Align)
            .Class(Class)
            // The panel scrolls, not the list, so the scrollbar sits inside the panel's rounded edge. `p-2!`
            // because the popover's own padding is p-4 and two paddings in one class list are decided by
            // stylesheet order, not by which was written last; the list drops the menu's padding in turn, the
            // same split UiMenuSurface makes, so the rows keep the inset they had.
            .PanelClass("max-h-96 overflow-y-auto p-2!")[
                UiThemePicker
                    .GroupName(GroupName)
                    .Themes(Themes)
                    .ShowSystem(ShowSystem)
                    .SystemLabel(SystemLabel)
                    .Class("w-52 flex-nowrap p-0")
            ];
}
