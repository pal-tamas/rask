namespace Rask;

public static partial class Ui
{
    /// <summary>
    ///     What scrolls when a <see cref="UiModal" />'s content is taller than the viewport. Unset, the dialog
    ///     does, and stays inside it; Flux names the one other answer.
    /// </summary>
    public enum ModalScroll
    {
        /// <summary>The whole layer: the panel runs past the bottom of the screen, so it is plain there is more.</summary>
        Body,
    }
}
