namespace Rask;

public static partial class Ui
{
    /// <summary>How a <see cref="UiButton" /> is drawn. Flux's <c>variant</c>.</summary>
    public enum ButtonVariant
    {
        /// <summary>A white surface with a hairline border. The default.</summary>
        Outline,

        /// <summary>Filled with the accent. One per view, mostly a form's submit.</summary>
        Primary,

        /// <summary>A tinted fill with no border.</summary>
        Filled,

        /// <summary>Red, for an action that destroys something.</summary>
        Danger,

        /// <summary>No surface until it is hovered.</summary>
        Ghost,

        /// <summary>As <see cref="Ghost" />, with a muted label.</summary>
        Subtle,
    }
}
