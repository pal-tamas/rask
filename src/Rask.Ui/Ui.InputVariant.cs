namespace Rask;

public static partial class Ui
{
    /// <summary>How a <see cref="UiInput{T}" /> is drawn.</summary>
    public enum InputVariant
    {
        /// <summary>A bordered white box.</summary>
        Outline = 0,

        /// <summary>A borderless tinted box — a value shown rather than asked for.</summary>
        Filled,
    }
}
