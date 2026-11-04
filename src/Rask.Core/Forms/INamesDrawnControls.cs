namespace Rask.Core.Forms;

/// <summary>
///     A component that draws form controls of its own, and is what the log calls them when one refuses an event
///     (<see cref="DisabledHandler" />): a kit slider's thumbs are <c>Ui.Slider</c>, not an <c>input</c>.
/// </summary>
internal interface INamesDrawnControls
{
    /// <summary>What the log calls the controls it draws: <c>Ui.Slider</c>.</summary>
    string Called { get; }

    /// <summary>The name they post under: the bound member's, or its own <c>Name</c>; null for none.</summary>
    string? PostsUnder { get; }
}
