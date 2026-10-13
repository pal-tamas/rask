namespace Rask;

public static partial class Ui
{
    /// <summary>What opens a <see cref="UiColorPicker" />: Flux's <c>type</c>.</summary>
    public enum ColorPickerType
    {
        /// <summary>A text input to type or paste a colour into, with a swatch button beside it. Flux's default.</summary>
        Input = 0,

        /// <summary>A button showing the colour and its value, for a toolbar or a swatches-only picker.</summary>
        Button,
    }
}
