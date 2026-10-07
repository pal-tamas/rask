namespace Rask;

public static partial class Ui
{
    /// <summary>The element a <see cref="UiLink" /> renders.</summary>
    public enum LinkAs
    {
        /// <summary>An <c>&lt;a&gt;</c>: it goes somewhere.</summary>
        A = 0,

        /// <summary>A <c>&lt;button type="button"&gt;</c> drawn as a link: it does something.</summary>
        Button,
    }
}
