namespace Rask;

public static partial class Ui
{
    /// <summary>What marks a picked row of a listbox <see cref="UiSelect{T}" />: Flux's <c>indicator</c>.</summary>
    public enum SelectIndicator
    {
        /// <summary>A checkmark beside the picked row, Flux's default.</summary>
        Check = 0,

        /// <summary>A checkbox beside every row, ticked in the picked ones.</summary>
        Checkbox,
    }
}
