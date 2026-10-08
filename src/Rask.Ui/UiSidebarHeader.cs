namespace Rask;

/// <summary>Flux's <c>flux:sidebar.header</c>: the top of a <see cref="UiSidebar" />, for its brand and its collapse control.</summary>
public sealed partial class UiSidebarHeader : Component
{
    /// <summary>Classes for the header row.</summary>
    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Div.Class(UiClass.Compose("flex min-h-10 items-center justify-between gap-2", Class))
            .Attributes(("data-ui-sidebar-header", ""))[Children ?? []];
}
