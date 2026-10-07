namespace Rask;

public static partial class Ui
{
    /// <summary>When a searchable <see cref="UiSelect{T}" /> empties its search: Flux's <c>clear</c>.</summary>
    public enum SelectClear
    {
        /// <summary>As soon as an option is picked, Flux's default.</summary>
        Select = 0,

        /// <summary>Only when the list closes, so several picks share one search.</summary>
        Close,
    }
}
