namespace Rask.Ui;

/// <summary>
/// The part of a <see cref="UiSidebar" /> that stays at the top while the navigation below it scrolls.
/// </summary>
/// <remarks>
/// Flux UI's <c>sidebar.header</c>. It is where the brand goes, and — on a narrow screen — the control that
/// slides the sidebar back out of the way. Nothing here scrolls: the header holds its place, the nav between
/// the header and the footer takes the leftover height, and a long list scrolls inside it.
/// </remarks>
public sealed partial class UiSidebarHeader : Component
{
    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Div.Class(UiClass.Compose("flex shrink-0 items-center gap-2", Class))[Children ?? []];
}
