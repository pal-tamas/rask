namespace Rask;

public static partial class Ui
{
    /// <summary>
    ///     Where a <see cref="UiContext" />'s menu opens against the pointer: below or above it, and which of the
    ///     menu's edges the pointer is on.
    /// </summary>
    public enum ContextPosition
    {
        /// <summary>Below the pointer, the menu's end edge on it. The default.</summary>
        BottomEnd,

        /// <summary>Below the pointer, centred on it.</summary>
        BottomCenter,

        /// <summary>Below the pointer, the menu's start edge on it.</summary>
        BottomStart,

        /// <summary>Above the pointer, the menu's end edge on it.</summary>
        TopEnd,

        /// <summary>Above the pointer, centred on it.</summary>
        TopCenter,

        /// <summary>Above the pointer, the menu's start edge on it.</summary>
        TopStart,
    }
}
