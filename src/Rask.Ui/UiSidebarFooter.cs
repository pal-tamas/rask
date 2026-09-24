namespace Rask;

/// <summary>
/// The part of a <see cref="UiSidebar" /> that stays at the bottom while the navigation above it scrolls.
/// </summary>
/// <remarks>
/// <para>
/// Flux UI's <c>sidebar.footer</c>, and where a <see cref="UiProfile" /> belongs. It is separated from the
/// navigation by a hairline and holds its place: a nav list long enough to scroll scrolls between the header
/// and this, rather than pushing the account row off the bottom of the screen.
/// </para>
/// <para>
/// It needs no <see cref="UiSpacer" /> in front of it. The sidebar gives the region between its header and its
/// footer the leftover height, so the footer is already at the bottom of a short sidebar as well as a long one.
/// </para>
/// </remarks>
public sealed partial class UiSidebarFooter : Component
{
    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Div.Class(UiClass.Compose(
            "mt-auto flex shrink-0 flex-col gap-1 border-t border-base-300 pt-3", Class))[Children ?? []];
}
