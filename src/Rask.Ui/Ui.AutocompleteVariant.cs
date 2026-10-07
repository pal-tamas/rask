namespace Rask;

public static partial class Ui
{
    /// <summary>How a <see cref="UiAutocomplete" />'s input is drawn: Flux's <c>variant</c>.</summary>
    public enum AutocompleteVariant
    {
        /// <summary>A bordered box, Flux's default.</summary>
        Outline = 0,

        /// <summary>A tinted box with no border.</summary>
        Filled,
    }
}
