namespace Rask;

public static partial class Ui
{
    /// <summary>How a <see cref="UiLink" /> shows that it is one.</summary>
    public enum LinkVariant
    {
        /// <summary>Underlined.</summary>
        Default = 0,

        /// <summary>Underlined only under the pointer.</summary>
        Ghost,

        /// <summary>Body-copy ink and no underline; it darkens under the pointer.</summary>
        Subtle,
    }
}
