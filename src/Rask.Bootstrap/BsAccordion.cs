namespace Rask.Bootstrap;

// A Bootstrap accordion: <div class="accordion">. Holds BsAccordionItem children; each item owns its
// open state (controlled), so the consumer decides single- vs multi-open by how it wires the items.
public sealed partial class BsAccordion : BsBlock
{
    // Removes the default background/borders (.accordion-flush).
    public bool? Flush { get; set; }

    protected override Component? Render() => Div
        .Id(Id)
        .Class(BsClass.Join("accordion", Flush is true ? "accordion-flush" : null, Class))[Items];
}

// One accordion item. Open shows the panel; OnToggle is wired to the header button (forwarded to the
// native Button, so flipping your Open state in the handler re-renders through the live runtime).
public sealed partial class BsAccordionItem : BsBlock
{
    public new string? Title { get; set; }
    public bool? Open { get; set; }
    public Action? OnToggle { get; set; }
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
