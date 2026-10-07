using System.Globalization;

namespace Rask;

/// <summary>
///     Flux's <c>flux:tooltip.content</c>: what a <see cref="UiTooltip" /> shows, when it is more than a line
///     of text.
/// </summary>
/// <remarks>
///     Written among the tooltip's children, after the trigger. <see cref="UiTooltip.Content" /> is the
///     shorthand that writes one for you.
/// </remarks>
public sealed partial class UiTooltipContent : Component
{
    // zinc-800 with white text; in dark zinc-700 inside a hairline of white. Flux draws no arrow and no shadow.
    // Where it sits in the flow, closed and open, is the stylesheet's (ui.css): it changes with the state.
    private const string Look =
        "m-0 rounded-md border-0 bg-zinc-800 px-2.5 py-2 text-xs font-medium text-white "
        + "[position-anchor:--ui-tooltip] dark:border dark:border-white/10 dark:bg-zinc-700";

    private static readonly UiTooltipScope Alone =
        new("", Ui.TooltipPosition.Top, Ui.TooltipAlign.Center, Gap: null, Offset: null, Toggled: false, Tooltip: true, Described: true);

    /// <summary>A keyboard shortcut shown after the content — <c>"⌘S"</c>.</summary>
    public string? Kbd { get; set; }

    /// <summary>Classes for the call site, added to the tooltip's own: a <c>max-w-*</c>, a <c>space-y-*</c>.</summary>
    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        var scope = Context.Get<UiTooltipScope>() ?? Alone;
        var content = Div
            .Class(Look, Place(scope.Position, scope.Align), Class)
            .Popover(scope.Toggled ? Rask.Core.Popover.Auto : Rask.Core.Popover.Manual)
            .Data("ui-tooltip-content", null);

        if (scope.Id.Length != 0)
        {
            content = content.Id(scope.Id);
        }

        if (Shift(scope) is { } style)
        {
            content = content.Style(style);
        }

        if (scope.Tooltip)
        {
            content = content.Role("tooltip");
        }

        // Read through the trigger's aria-describedby, which reaches a hidden element; left in the reading
        // order as well, a screen reader would say it twice.
        if (scope.Described)
        {
            content = content.Aria("hidden", "true");
        }

        return Kbd is { } kbd
            ? content[Children, " ", Span.Class("ps-1 text-zinc-300")[kbd]]
            : content[Children ?? []];
    }

    // The side is CSS anchor positioning's position-area. The inset towards the trigger is Flux's gap of 5px
    // and the insets on the other sides keep it that far inside the viewport — insets rather than margins,
    // so the box itself carries nothing. Flipping to the opposite side when it does not fit is the
    // platform's, and flips the insets with it.
    private static string Place(Ui.TooltipPosition position, Ui.TooltipAlign align) => (position, align) switch
    {
        (Ui.TooltipPosition.Top, Ui.TooltipAlign.Center) => "inset-[5px] [position-area:top] [position-try-fallbacks:flip-block]",
        (Ui.TooltipPosition.Top, Ui.TooltipAlign.Start) => "inset-x-0 inset-y-[5px] [position-area:block-start_span-inline-end] [position-try-fallbacks:flip-block]",
        (Ui.TooltipPosition.Top, _) => "inset-x-0 inset-y-[5px] [position-area:block-start_span-inline-start] [position-try-fallbacks:flip-block]",
        (Ui.TooltipPosition.Bottom, Ui.TooltipAlign.Center) => "inset-[5px] [position-area:bottom] [position-try-fallbacks:flip-block]",
        (Ui.TooltipPosition.Bottom, Ui.TooltipAlign.Start) => "inset-x-0 inset-y-[5px] [position-area:block-end_span-inline-end] [position-try-fallbacks:flip-block]",
        (Ui.TooltipPosition.Bottom, _) => "inset-x-0 inset-y-[5px] [position-area:block-end_span-inline-start] [position-try-fallbacks:flip-block]",
        (Ui.TooltipPosition.Left, Ui.TooltipAlign.Center) => "inset-[5px] [position-area:left] [position-try-fallbacks:flip-inline]",
        (Ui.TooltipPosition.Left, Ui.TooltipAlign.Start) => "inset-x-[5px] inset-y-0 [position-area:left_span-bottom] [position-try-fallbacks:flip-inline]",
        (Ui.TooltipPosition.Left, _) => "inset-x-[5px] inset-y-0 [position-area:left_span-top] [position-try-fallbacks:flip-inline]",
        (Ui.TooltipPosition.Right, Ui.TooltipAlign.Center) => "inset-[5px] [position-area:right] [position-try-fallbacks:flip-inline]",
        (Ui.TooltipPosition.Right, Ui.TooltipAlign.Start) => "inset-x-[5px] inset-y-0 [position-area:right_span-bottom] [position-try-fallbacks:flip-inline]",
        _ => "inset-x-[5px] inset-y-0 [position-area:right_span-top] [position-try-fallbacks:flip-inline]",
    };

    // Gap and Offset are numbers, so they are the two things a class cannot say.
    private static string? Shift(UiTooltipScope scope)
    {
        var gap = scope.Gap is { } pixels ? Facing(scope.Position) + pixels.ToString(CultureInfo.InvariantCulture) + "px" : null;
        if (scope.Offset is not { } offset || offset == 0)
        {
            return gap;
        }

        // Measured on Flux: an offset slides it away from the edge it is aligned to, so End runs the other way.
        var slide = (scope.Align == Ui.TooltipAlign.End ? -offset : offset).ToString(CultureInfo.InvariantCulture) + "px";
        var beside = scope.Position is Ui.TooltipPosition.Left or Ui.TooltipPosition.Right;
        var translate = beside ? "translate:0 " + slide : "translate:" + slide + " 0";
        return gap is null ? translate : gap + ";" + translate;
    }

    // The inset on the side that faces the trigger.
    private static string Facing(Ui.TooltipPosition position) => position switch
    {
        Ui.TooltipPosition.Top => "bottom:",
        Ui.TooltipPosition.Bottom => "top:",
        Ui.TooltipPosition.Left => "right:",
        _ => "left:",
    };
}
