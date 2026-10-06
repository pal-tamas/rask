namespace Rask;

/// <summary>
///     Flux's <c>flux:accordion</c>: a stack of <see cref="UiAccordionItem" />s that open and close.
/// </summary>
/// <remarks>
///     <para>
///     Each item is a native <c>&lt;details&gt;</c>, so it opens with a click, Enter or Space with no handler
///     and no script, and the browser's find-in-page opens the item holding a match.
///     </para>
///     <code>
///     Ui.Accordion.Exclusive()[
///         Ui.AccordionItem.Heading("Where do you ship?")["The United States and Canada."],
///         Ui.AccordionItem.Heading("Can I change my order?").Expanded()["Until it has shipped."]
///     ]
///     </code>
/// </remarks>
public sealed partial class UiAccordion : Component
{
    private static readonly Dictionary<string, string?> Marks = new(StringComparer.Ordinal) { ["ui-accordion"] = "" };

    // `data-transition` is what ui.css keys the quarter-second transition on: it is drawn on the items'
    // ::details-content, which no class on the item can reach.
    private static readonly Dictionary<string, string?> Animated = new(StringComparer.Ordinal)
    {
        ["ui-accordion"] = "",
        ["transition"] = "",
    };

    private string? _group;

    /// <summary>Which side of the heading the chevron is on. After it when unset.</summary>
    public Ui.AccordionVariant? Variant { get; set; }

    /// <summary>Opens and closes items over a quarter of a second rather than at once.</summary>
    public bool? Transition { get; set; }

    /// <summary>Opening one item closes the others.</summary>
    public bool? Exclusive { get; set; }

    /// <summary>Classes for the call site, added to the accordion's own.</summary>
    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        var transition = Transition == true;
        var scope = new UiAccordionScope(
            Exclusive == true ? _group ??= $"ui-accordion-{UiInstanceCounter.Next()}" : null,
            Variant == Ui.AccordionVariant.Reverse,
            transition,
            Disabled: false);

        return Div.Class("block", Class).Data(transition ? Animated : Marks)[
            Context.Provide(scope)[Children ?? []]
        ];
    }
}
