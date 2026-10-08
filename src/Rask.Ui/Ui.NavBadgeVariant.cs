namespace Rask;

public static partial class Ui
{
    /// <summary>
    ///     How the badge on a <see cref="UiNavbarItem" /> or a <see cref="UiNavlistItem" /> is drawn. Flux's <c>badge:variant</c>.
    /// </summary>
    public enum NavBadgeVariant
    {
        /// <summary>Tinted with its colour. The default.</summary>
        Solid,

        /// <summary>A hairline of its colour around a clear centre.</summary>
        Outline,
    }
}
