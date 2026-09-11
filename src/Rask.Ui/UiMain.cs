namespace Rask.Ui;

/// <summary>The console's content column, inside the frame.</summary>
/// <remarks>
/// It spaces its own sections, so a page hands it a heading, a banner, a table and a card and writes no margin
/// between them. A gap rather than a margin on each section: a section that renders nothing — a banner with no
/// message — takes no room, where a margin on it would have left a hole.
/// </remarks>
public sealed partial class UiMain : Component
{
    /// <inheritdoc />
    protected override Component? Render() =>
        Main.Class("flex flex-col gap-4 bg-base-200 px-3 py-4 sm:gap-6 sm:px-5 sm:py-6")[Children ?? []];
}
