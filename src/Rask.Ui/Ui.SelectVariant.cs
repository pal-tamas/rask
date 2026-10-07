namespace Rask;

public static partial class Ui
{
    /// <summary>How a <see cref="UiSelect{T}" /> is drawn: Flux's <c>variant</c>.</summary>
    public enum SelectVariant
    {
        /// <summary>The browser's own <c>&lt;select&gt;</c>, Flux's default.</summary>
        Default = 0,

        /// <summary>A button that opens a drawn list: options with icons, descriptions, a search, several answers.</summary>
        Listbox,

        /// <summary>A text input that filters the list under it as it is typed in.</summary>
        Combobox,
    }
}
