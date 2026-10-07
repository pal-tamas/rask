namespace Rask;

public static partial class Ui
{
    /// <summary>Where along its side a <see cref="UiTooltip" /> sits. Flux's <c>align</c>.</summary>
    public enum TooltipAlign
    {
        /// <summary>Centred on the trigger. The default.</summary>
        Center,

        /// <summary>Flush with the trigger's start edge: its reading-start side, or its top when beside it.</summary>
        Start,

        /// <summary>Flush with the trigger's end edge: its reading-end side, or its bottom when beside it.</summary>
        End,
    }
}
