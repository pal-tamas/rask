namespace Rask;

public static partial class Ui
{
    /// <summary>
    ///     Where an item's content sits beside its indicator in a <see cref="UiTimeline" />: Flux's <c>align</c>.
    /// </summary>
    /// <remarks>
    ///     Across the timeline's direction — up and down beside the indicator in a vertical one, left to right
    ///     under it in a horizontal one, where <see cref="Baseline" /> reads as <see cref="Start" />.
    /// </remarks>
    public enum TimelineAlign
    {
        /// <summary>The indicator against the middle of the content. The default.</summary>
        Center = 0,

        /// <summary>The indicator and the content start together.</summary>
        Start,

        /// <summary>The indicator's text on the baseline of the content's first line.</summary>
        Baseline,

        /// <summary>The indicator and the content end together.</summary>
        End,
    }
}
