namespace Rask;

public static partial class Ui
{
    /// <summary>How a <see cref="UiToggle" /> is drawn, to match the controls beside it.</summary>
    public enum ToggleVariant
    {
        /// <summary>A bordered white surface with a faint shadow. Flux's default.</summary>
        Outline,

        /// <summary>A tinted surface, no border.</summary>
        Filled,

        /// <summary>Nothing until it is hovered.</summary>
        Ghost,

        /// <summary>A ghost in quieter ink.</summary>
        Subtle,
    }
}
