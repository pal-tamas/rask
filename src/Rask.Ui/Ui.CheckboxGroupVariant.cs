namespace Rask;

public static partial class Ui
{
    /// <summary>How a <see cref="UiCheckboxGroup{T}" /> draws its checkboxes.</summary>
    public enum CheckboxGroupVariant
    {
        /// <summary>One per row: the box, then its label.</summary>
        Default = 0,

        /// <summary>Each a bordered card with room for a description.</summary>
        Cards,

        /// <summary>Each a rounded pill, wrapping onto as many rows as it needs.</summary>
        Pills,

        /// <summary>Each a button, in a row.</summary>
        Buttons,
    }
}
