namespace Rask;

public static partial class Ui
{
    /// <summary>How a <see cref="UiPhone" /> is drawn: Flux's input variants.</summary>
    public enum PhoneVariant
    {
        /// <summary>A white box with a border, Flux's default.</summary>
        Outline = 0,

        /// <summary>A tinted box with no border, where other controls of the form are filled.</summary>
        Filled,
    }
}
