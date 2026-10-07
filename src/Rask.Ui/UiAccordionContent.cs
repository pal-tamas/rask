namespace Rask;

/// <summary>
///     Flux's <c>flux:accordion.content</c>: what a <see cref="UiAccordionItem" /> shows while it is open.
/// </summary>
/// <remarks>
///     Closed, it is still in the document, so the browser's find-in-page reaches it and opens the item.
/// </remarks>
public sealed partial class UiAccordionContent : Component
{
    /// <summary>Classes for the call site, added to the content's own.</summary>
    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        var scope = Context.Get<UiAccordionScope>() ?? UiAccordionScope.None;

        // As tall as the <details>' own content box, which ui.css holds at nothing while the item is closed.
        return Div.Class(scope.Transition ? "block h-full overflow-hidden group-open/accordion-item:overflow-visible" : "block h-full")
            .Data("ui-accordion-content", "")[
            Div.Class("pt-2 text-sm text-zinc-500 dark:text-zinc-300", Class)[Children ?? []]
        ];
    }
}
