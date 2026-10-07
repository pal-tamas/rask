namespace Rask;

/// <summary>
///     Flux's <c>flux:accordion.item</c>: one heading and the content it opens.
/// </summary>
/// <remarks>
///     <para>
///     <see cref="Heading" /> is the shorthand — the children are then the content. Without it the children
///     are a <see cref="UiAccordionHeading" /> and a <see cref="UiAccordionContent" />.
///     </para>
///     <para>
///     The browser opens and closes it. <see cref="Expanded" /> is the state it starts in.
///     </para>
/// </remarks>
public sealed partial class UiAccordionItem : Component
{
    private const string Classes =
        "group/accordion-item block border-b border-zinc-800/10 pt-4 pb-4 first:pt-0 last:border-b-0 last:pb-0 dark:border-white/10";

    /// <summary>The heading's text, in place of a <see cref="UiAccordionHeading" /> child.</summary>
    public string? Heading { get; set; }

    /// <summary>Whether the item is open. Closed when unset.</summary>
    public bool? Expanded { get; set; }

    /// <summary>The item cannot be opened or closed.</summary>
    public bool? Disabled { get; set; }

    /// <summary>Classes for the call site, added to the item's own.</summary>
    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        var scope = Context.Get<UiAccordionScope>() ?? UiAccordionScope.None;
        var item = Details.Class(Classes, Class).Data("ui-accordion-item", "").Name(scope.Group).Open(Expanded == true);
        var inside = Context.Provide(scope with { Disabled = Disabled == true });
        return item[
            Heading is null
                ? inside[Children ?? []]
                : inside[Ui.AccordionHeading[Heading], Ui.AccordionContent[Children ?? []]]
        ];
    }
}
