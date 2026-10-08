namespace Rask;

public static partial class Ui
{
    /// <summary>
    ///     The edge of the viewport a flyout <see cref="UiModal" /> opens from.
    /// </summary>
    public enum ModalPosition
    {
        /// <summary>The right edge, full height. The default.</summary>
        Right,

        /// <summary>The left edge, full height.</summary>
        Left,

        /// <summary>The bottom edge, full width.</summary>
        Bottom,
    }
}
