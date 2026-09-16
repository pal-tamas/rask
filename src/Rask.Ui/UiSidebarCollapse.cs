namespace Rask.Ui;

/// <summary>
/// The control that narrows a docked <see cref="UiSidebar" /> to a rail of icons, and widens it again.
/// </summary>
/// <remarks>
/// <para>
/// Flux UI's <c>sidebar.collapse</c>. A <c>&lt;label&gt;</c> for the sidebar's rail checkbox, so it works with
/// no runtime at all — the same mechanism as <see cref="UiSidebarToggle" />, and given the same
/// <c>role="button"</c> and <c>tabindex</c> so a keyboard reaches it.
/// </para>
/// <para>
/// It is the opposite of the toggle in where it belongs: the toggle is for a phone, where the sidebar slides
/// over the page, and hides once the sidebar docks; this one is only useful once it HAS docked, so it appears
/// from the same breakpoint the toggle disappears at. Give it the sidebar's own <c>Collapsible</c> breakpoint.
/// </para>
/// </remarks>
public sealed partial class UiSidebarCollapse : Component
{
    /// <summary>The <see cref="UiSidebar.Id" /> of the sidebar it narrows.</summary>
    public required string For { get; set; }

    /// <summary>The width from which the sidebar is docked, so the control appears. Shown at every width if unset.</summary>
    public UiBreakpoint? Collapsible { get; set; }

    /// <summary>What a screen reader announces. "Collapse sidebar" unless this says otherwise.</summary>
    public string? AccessibleLabel { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        RaskMarkup.Label
            .For(For + "-rail")
            .Class(UiClass.Compose(
                "btn btn-ghost btn-square btn-sm",
                Collapsible is { } from ? "hidden" : "",
                Collapsible is { } shown ? UiClassNames.ShownFrom(shown) : "",
                Class))
            .Role("button")
            .TabIndex(0)
            .Aria("label", AccessibleLabel ?? "Collapse sidebar")[
            UiIcon.Name(UiIconName.ChevronRight).Class("ui-rail-flip size-4 shrink-0")
        ];
}
