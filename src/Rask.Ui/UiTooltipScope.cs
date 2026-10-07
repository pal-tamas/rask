namespace Rask;

/// <summary>What a tooltip tells its content, which the call site writes among the tooltip's children.</summary>
/// <param name="Id">The id that joins the trigger to the content.</param>
/// <param name="Position">Which side of the trigger the content opens on.</param>
/// <param name="Align">Where along that side it sits.</param>
/// <param name="Gap">The distance from the trigger in pixels, when it is not Flux's 5.</param>
/// <param name="Offset">How far it is slid along that side, in pixels.</param>
/// <param name="Toggled">Whether a click on the trigger opens it, rather than a hover.</param>
/// <param name="Tooltip">Whether it is a tooltip to a screen reader (<c>role="tooltip"</c>): Flux's toggleable one is not.</param>
/// <param name="Described">Whether it is read through the trigger, and so kept out of the reading order itself.</param>
internal sealed record UiTooltipScope(
    string Id,
    Ui.TooltipPosition Position,
    Ui.TooltipAlign Align,
    int? Gap,
    int? Offset,
    bool Toggled,
    bool Tooltip,
    bool Described);
