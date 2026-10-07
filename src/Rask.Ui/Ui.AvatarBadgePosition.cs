namespace Rask;

public static partial class Ui
{
    /// <summary>
    ///     The corner of a <see cref="UiAvatar" /> its badge sits on. Flux's <c>badge:position</c>.
    /// </summary>
    public enum AvatarBadgePosition
    {
        /// <summary>The default.</summary>
        BottomRight,

        /// <summary><c>top left</c>.</summary>
        TopLeft,

        /// <summary><c>top right</c>.</summary>
        TopRight,

        /// <summary><c>bottom left</c>.</summary>
        BottomLeft,
    }
}
