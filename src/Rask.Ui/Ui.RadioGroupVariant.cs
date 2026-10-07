namespace Rask;

public static partial class Ui
{
    /// <summary>How a <see cref="UiRadioGroup{T}" /> draws its radios.</summary>
    public enum RadioGroupVariant
    {
        /// <summary>One per row: the dot, then its label.</summary>
        Default = 0,

        /// <summary>One joined strip, the chosen segment raised.</summary>
        Segmented,

        /// <summary>Each a bordered card with room for a description.</summary>
        Cards,

        /// <summary>Each a rounded pill.</summary>
        Pills,

        /// <summary>Each a button, in a row.</summary>
        Buttons,
    }
}
