namespace Rask.Ui;

/// <summary>
/// <see cref="UiThemePicker" /> behind a trigger, for a bar with no room for thirty-five radios.
/// </summary>
/// <remarks>
/// <para>
/// A <c>&lt;details&gt;</c> rather than a popover or a menu button, because it opens and closes with no
/// JavaScript — the same constraint the picker itself is built to.
/// </para>
/// <para>
/// It is not built on <see cref="UiDropdown" />, and the reason is structural rather than stylistic:
/// that component supplies its own <c>&lt;ul class="menu"&gt;</c> to hold children, and the picker is
/// already a <c>&lt;ul&gt;</c>. Composing them would put one list directly inside another, which is not
/// valid HTML — so this renders the disclosure itself and hands the picker the content classes.
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

    /// <summary>Alignment, as one of daisyUI's placement classes (for example <c>dropdown-end</c>).</summary>
    public string? Placement { get; set; }

    /// <summary>The radio group's name, passed through to the picker.</summary>
    public string GroupName { get; set; } = "rask-ui-theme";

    /// <summary>The themes to offer. Defaults to every theme the kit ships.</summary>
    public IReadOnlyList<UiThemeName>? Themes { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Details.Class(UiClass.Compose("dropdown", Placement, Class))[
            Summary.Class("btn btn-sm")[
                UiIcon.Name(UiIconName.Sparkles).Class("size-4 shrink-0"),
                Span[Trigger]
            ],
            UiThemePicker
                .GroupName(GroupName)
                .Themes(Themes)
                .Class("dropdown-content z-1 max-h-96 w-52 flex-nowrap overflow-y-auto rounded-box "
                       + "bg-base-100 p-2 shadow-sm")
        ];
}
