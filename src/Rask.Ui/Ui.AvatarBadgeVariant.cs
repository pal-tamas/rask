namespace Rask;

public static partial class Ui
{
    /// <summary>
    ///     How a <see cref="UiAvatar" />'s badge is drawn. Flux's <c>badge:variant</c>.
    /// </summary>
    public enum AvatarBadgeVariant
    {
        /// <summary>Filled with its colour. The default.</summary>
        Solid,

        /// <summary>A ring of its colour around a centre the colour of the page.</summary>
        Outline,
    }
}
