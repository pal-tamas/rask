namespace Rask;

public static partial class Ui
{
    /// <summary>How much a <see cref="UiText" /> stands out from the page.</summary>
    public enum TextVariant
    {
        /// <summary>Body copy.</summary>
        Default = 0,

        /// <summary>The ink a heading takes, for the part that matters.</summary>
        Strong,

        /// <summary>Lighter than body copy, for the part that can be skipped.</summary>
        Subtle,
    }
}
