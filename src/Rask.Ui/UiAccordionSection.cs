namespace Rask.Ui;

/// <summary>
/// One titled section of a <see cref="UiAccordion" />.
/// </summary>
/// <remarks>
/// Its <c>Key</c> is its identity in the accordion, so a section without one cannot be opened. It reads
/// the open state from the accordion above it and refuses to render outside one, rather than drawing a
/// section that silently never opens.
/// </remarks>
public sealed partial class UiAccordionSection : Component
{
    /// <summary>The heading, and the thing you press.</summary>
    public required string Title { get; set; }

    /// <summary>Draws the arrow or plus marker.</summary>
    public UiMarker? Marker { get; set; }

    public string? Class { get; set; }

    /// <inheritdoc />
    protected override Component? Render()
    {
        // Not Context.Required, whose message would name Context.Provide<UiAccordionState> — machinery
        // nobody writes. The mistake this catches is a section outside an accordion, and the sentence a
        // reader needs names the two components they typed.
        var state = Context.Get<UiAccordionState>()
            ?? throw new InvalidOperationException(
                $"UiAccordionSection \"{Title}\" is not inside a UiAccordion. Put it in one: "
                + "UiAccordion.Open(key).OnOpen(k => …)[ UiAccordionSection.Key(\"a\").Title(\"…\")[ … ] ].");

        var key = Key as string;
        var open = key is not null && state.Open == key;

        var title = Button
            .Type("button")
            .Class("collapse-title flex w-full items-center text-left font-semibold")
            .Aria(new Dictionary<string, string?> { ["expanded"] = open ? "true" : "false" });

        if (state.OnOpen is { } onOpen)
        {
            title = title.OnClick(() => onOpen.Invoke(open ? null : key) ?? Task.CompletedTask);
        }

        return Div.Class(UiClass.Compose(
            "collapse join-item border border-base-300 bg-base-100",
            Marker is { } marker ? UiClassNames.Marker(marker) : "",
            // Both, for the same reason UiDropdown writes dropdown-close: `collapse` opens on
            // :focus-within too, so omitting collapse-open is not the same as being closed.
            open ? "collapse-open" : "collapse-close",
            Class))[
            title[Title],
            Div.Class("collapse-content text-sm")[Children ?? []]
        ];
    }
}
