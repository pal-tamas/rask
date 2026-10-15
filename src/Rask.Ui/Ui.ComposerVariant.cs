namespace Rask;

public static partial class Ui
{
    /// <summary>How a <see cref="UiComposer{T}" /> is drawn.</summary>
    public enum ComposerVariant
    {
        /// <summary>The message box, with large round corners.</summary>
        Default = 0,

        /// <summary>The corners of the other form inputs.</summary>
        Input,
    }
}
