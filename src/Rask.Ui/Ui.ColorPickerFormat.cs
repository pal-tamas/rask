namespace Rask;

public static partial class Ui
{
    /// <summary>What a <see cref="UiColorPicker" /> writes its colour as: Flux's <c>format</c>.</summary>
    public enum ColorPickerFormat
    {
        /// <summary><c>#bada55</c>, Flux's default.</summary>
        Hex = 0,

        /// <summary><c>#bada55ff</c>: hex with its alpha, and an alpha slider in the popover.</summary>
        Hexa,

        /// <summary><c>rgb(186 218 85)</c>.</summary>
        Rgb,

        /// <summary><c>rgb(186 218 85 / 0.5)</c>, with an alpha slider.</summary>
        Rgba,

        /// <summary><c>hsl(74 64% 59%)</c>.</summary>
        Hsl,

        /// <summary><c>hsl(74 64% 59% / 0.5)</c>, with an alpha slider.</summary>
        Hsla,
    }
}
