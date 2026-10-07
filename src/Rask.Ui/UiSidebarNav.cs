namespace Rask;

/// <summary>Flux's <c>flux:sidebar.nav</c>: a list of <see cref="UiSidebarItem" />s and <see cref="UiSidebarGroup" />s, a navigation landmark.</summary>
public sealed partial class UiSidebarNav : Component
{
    /// <summary>Classes for the list.</summary>
    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Nav.Class(UiClass.Compose("flex flex-col", Class))
            .Attributes(("data-ui-sidebar-nav", ""))[Children ?? []];
}
