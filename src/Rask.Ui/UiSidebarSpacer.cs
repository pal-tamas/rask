namespace Rask;

/// <summary>Flux's <c>flux:sidebar.spacer</c>: takes the leftover height, pushing what follows to the bottom of a <see cref="UiSidebar" />.</summary>
public sealed partial class UiSidebarSpacer : Component
{
    /// <summary>Classes for the spacer.</summary>
    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Div.Class(UiClass.Compose("flex-1", Class)).Attributes(("data-ui-sidebar-spacer", ""));
}
