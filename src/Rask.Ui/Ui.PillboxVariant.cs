namespace Rask;

public static partial class Ui
{
    /// <summary>What a <see cref="UiPillbox{T}" /> is opened from: Flux's <c>variant</c>.</summary>
    public enum PillboxVariant
    {
        /// <summary>The pills alone, Flux's default: the list opens from the box they sit in.</summary>
        Default = 0,

        /// <summary>An input among the pills, typed into to narrow the list.</summary>
        Combobox,
    }
}
