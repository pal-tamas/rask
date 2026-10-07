namespace Rask;

/// <summary>
///     Flux's <c>flux:accordion.heading</c>: the line of a <see cref="UiAccordionItem" /> that opens it.
/// </summary>
/// <remarks>
///     A <c>&lt;summary&gt;</c>, so it is in the tab order and Enter and Space toggle the item. In a disabled
///     item it leaves the tab order, takes no pointer and says <c>aria-disabled</c>.
/// </remarks>
public sealed partial class UiAccordionHeading : Component
{
    private const string Shown = "block group-open/accordion-item:hidden";
    private const string ShownOpen = "hidden text-zinc-800 group-open/accordion-item:block dark:text-white";

    /// <summary>Classes for the call site, added to the heading's own.</summary>
    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        var scope = Context.Get<UiAccordionScope>() ?? UiAccordionScope.None;
        var heading = Summary.Class(Classes(scope.Reverse, scope.Disabled), Class).Data("ui-accordion-heading", "");

        if (scope.Disabled)
        {
            heading = heading.TabIndex(-1).Aria("disabled", "true");
        }

        var side = scope.Reverse ? "me-2" : "ms-6";
        return heading[
            Span.Class("flex-1")[Children ?? []],
            Ui.Icon.Name(scope.Reverse ? Ui.IconName.ChevronDown : Ui.IconName.ChevronUp).Mini.Class(side, ShownOpen),
            Ui.Icon.Name(scope.Reverse ? Ui.IconName.ChevronRight : Ui.IconName.ChevronDown).Mini
                .Class(side, Shown, Closed(scope.Disabled))
        ];
    }

    // The marker a <summary> draws is its list-item box, which `flex` replaces; Safari draws its own besides.
    private static string Classes(bool reverse, bool disabled) => (reverse, disabled) switch
    {
        (false, false) => "group/accordion-heading flex w-full cursor-pointer items-center justify-between text-start text-sm font-medium text-zinc-800 dark:text-white [&::-webkit-details-marker]:hidden",
        (true, false) => "group/accordion-heading flex w-full cursor-pointer flex-row-reverse items-center justify-end text-start text-sm font-medium text-zinc-800 dark:text-white [&::-webkit-details-marker]:hidden",
        (false, true) => "pointer-events-none flex w-full cursor-default items-center justify-between text-start text-sm font-medium text-zinc-400 [&::-webkit-details-marker]:hidden",
        (true, true) => "pointer-events-none flex w-full cursor-default flex-row-reverse items-center justify-end text-start text-sm font-medium text-zinc-400 [&::-webkit-details-marker]:hidden",
    };

    // Pale until the heading is pointed at; a disabled heading is never pointed at.
    private static string Closed(bool disabled) => disabled
        ? "text-zinc-300 dark:text-zinc-400"
        : "text-zinc-300 group-hover/accordion-heading:text-zinc-800 dark:text-zinc-400 dark:group-hover/accordion-heading:text-white";
}
