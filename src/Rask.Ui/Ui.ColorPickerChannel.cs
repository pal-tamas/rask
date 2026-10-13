namespace Rask;

public static partial class Ui
{
    /// <summary>What a <see cref="UiColorPickerSlider" /> moves: Flux's <c>channel</c>.</summary>
    public enum ColorPickerChannel
    {
        /// <summary>The hue, 0 to 360. Flux's default.</summary>
        Hue = 0,

        /// <summary>The opacity, 0 to 100.</summary>
        Alpha,
    }
}
