namespace Rask;

public static partial class Ui
{
    /// <summary>
    ///     The element a <see cref="UiAvatar" /> is written as. Flux's <c>as</c>.
    /// </summary>
    public enum AvatarAs
    {
        /// <summary>A <c>&lt;div&gt;</c>. The default.</summary>
        Div,

        /// <summary>A <c>&lt;button type="button"&gt;</c>, for an avatar that opens something.</summary>
        Button,
    }
}
