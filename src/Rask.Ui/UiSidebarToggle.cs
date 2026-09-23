namespace Rask;

/// <summary>
/// The button that slides a collapsed <see cref="UiSidebar" /> in and out — the hamburger in a phone's top bar.
/// </summary>
/// <remarks>
/// <para>
/// A <c>&lt;label&gt;</c> for the sidebar's checkbox, which is what opens it with no runtime. A label is not a
/// keyboard stop of its own, so it is given one: <c>role="button"</c> and <c>tabindex="0"</c>, and the runtime
/// presses a focused button-label on Enter and Space the way a real button is pressed.
/// </para>
/// <para>
/// It hides from the sidebar's <see cref="Collapsible" /> breakpoint up, where the sidebar is docked and there is
/// nothing to toggle. Give it the same breakpoint the sidebar has.
/// </para>
/// </remarks>
public sealed partial class UiSidebarToggle : Component
{
    /// <summary>The <see cref="UiSidebar.Id" /> of the sidebar it toggles.</summary>
    public required string For { get; set; }

    /// <summary>The width from which the sidebar is docked, so the toggle is hidden. Shown at every width if unset.</summary>
    public Ui.Breakpoint? Collapsible { get; set; }

    /// <summary>What a screen reader announces. "Toggle sidebar" unless this says otherwise.</summary>
    public string? AccessibleLabel { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        RaskMarkup.Label
            .For(For)
            .Class(UiClass.Compose(
                "btn btn-ghost btn-square drawer-button",
                Collapsible is { } from ? UiClassNames.HiddenFrom(from) : "",
                Class))
            .Role("button")
            .TabIndex(0)
            .Aria("label", AccessibleLabel ?? "Toggle sidebar")[
            Ui.Icon.Name(Ui.IconName.Menu).Class("size-5 shrink-0")
        ];
}
