namespace Rask;

public static partial class Ui
{
    /// <summary>
    ///     What scrolls when a <see cref="UiModal" />'s content is taller than the viewport.
    /// </summary>
    public enum ModalScroll
    {
        /// <summary>The dialog, which stays inside the viewport. The default.</summary>
        Dialog,

        /// <summary>The whole layer: the panel runs past the bottom of the screen, so it is plain there is more.</summary>
        Body,
    }
}
