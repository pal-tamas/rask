namespace Rask;

public static partial class Ui
{
    /// <summary>The element a <see cref="UiInput{T}" /> renders as.</summary>
    public enum InputAs
    {
        /// <summary>An <c>&lt;input&gt;</c>.</summary>
        Input = 0,

        /// <summary>A <c>&lt;button&gt;</c> that looks like the input — the trigger of a command palette.</summary>
        Button,
    }
}
