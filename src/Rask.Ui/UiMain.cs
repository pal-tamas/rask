namespace Rask.Ui;

/// <summary>The console's content column, inside the frame.</summary>
/// <remarks>
/// It spaces its own sections, so a page hands it a heading, a banner, a table and a card and writes no margin
/// between them. A gap rather than a margin on each section: a section that renders nothing — a banner with no
/// message — takes no room, where a margin on it would have left a hole.
/// </remarks>
public sealed partial class UiMain : Component
{
    /// <summary>
    ///     Holds the content to a readable width and centres it, instead of letting it run the full window.
    /// </summary>
    /// <remarks>
    ///     Flux UI's <c>main container</c>. Off by default, because a console's tables and grids want every pixel;
    ///     on for a page of prose or a settings form, where a line that crosses a wide monitor is unreadable.
    /// </remarks>
    public bool? Container { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render() =>
        Main.Class(UiClass.Compose(
            "flex flex-col gap-4 bg-base-200 px-3 py-4 sm:gap-6 sm:px-5 sm:py-6",
            Container == true ? "mx-auto w-full max-w-5xl" : "",
            Class))[
            Children ?? []
        ];
}
