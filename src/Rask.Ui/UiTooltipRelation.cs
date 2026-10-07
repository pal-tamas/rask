namespace Rask;

/// <summary>How a tooltip's trigger stands to its content, which decides the ARIA Flux writes on the trigger.</summary>
internal enum UiTooltipRelation
{
    /// <summary>The content describes the trigger, or names one that has no words of its own.</summary>
    Describes,

    /// <summary>The content is the trigger's to show (<c>interactive</c>): <c>aria-controls</c> and <c>aria-expanded</c>.</summary>
    Controls,

    /// <summary>A click on the trigger opens the content (<c>toggleable</c> around a button).</summary>
    Toggles,
}
