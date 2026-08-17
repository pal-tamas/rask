namespace Rask.Bootstrap;

// A Bootstrap accordion: <div class="accordion">. Holds BsAccordionItem children; each item owns its
// open state (controlled), so the consumer decides single- vs multi-open by how it wires the items.

/// <summary>
///     A stack of disclosure panels. By default opening one closes the others — reach for it when the
///     sections are alternatives rather than a checklist.
/// </summary>
public sealed partial class BsAccordion : BsBlock
{
    // Removes the default background/borders (.accordion-flush).

    /// <summary>Removes the outer borders and rounding, to sit flush inside a parent container.</summary>
    public bool? Flush { get; set; }

    protected override Component? Render() => Div
        .Id(Id)
        .Class(BsClass.Join("accordion", Flush is true ? "accordion-flush" : null, Class))[Items];
}

// One accordion item. Open shows the panel; OnToggle is wired to the header button (forwarded to the
// native Button, so flipping your Open state in the handler re-renders through the live runtime).

/// <summary>
///     One panel of an accordion: a header that toggles, and the content it reveals.
/// </summary>
public sealed partial class BsAccordionItem : BsBlock
{
    /// <summary>The header text, which is also the toggle.</summary>
    public new string? Title { get; set; }

    /// <summary>Whether this panel starts expanded.</summary>
    public bool? Open { get; set; }

    /// <summary>Runs when the panel is opened or closed.</summary>
    public Action? OnToggle { get; set; }

    /// <summary>Runs when the panel is opened or closed, asynchronously.</summary>
    public Func<Task>? OnToggleAsync { get; set; }

    protected override Component? Render()
    {
        var open = Open is true;
        var expanded = new Dictionary<string, string?> { ["expanded"] = open ? "true" : "false" };

        return Div.Id(Id).Class(BsClass.Join("accordion-item", Class))[
            H2.Class("accordion-header")[
                Button
                    .Type("button")
                    .Class(BsClass.Join("accordion-button", open ? null : "collapsed"))
                    .Aria(expanded)
                    .OnClick(OnToggle)
                    .OnClickAsync(OnToggleAsync)[Title ?? ""]
            ],
            Div.Class(BsClass.Join("accordion-collapse", "collapse", open ? "show" : null))[
                Div.Class("accordion-body")[Items]
            ]
        ];
    }
}
