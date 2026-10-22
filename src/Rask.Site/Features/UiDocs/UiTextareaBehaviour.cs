namespace Rask.Site.Features.UiDocs;

/// <summary>How a textarea behaves: its height, and its value written as an input's is.</summary>
internal sealed partial class UiTextareaBehaviour : Component
{
    /// <inheritdoc />
    protected override Component? Render() =>
        UiDocsBehaviour[
            Height(),
            Value()
        ];

    private static UiDocsTopic Height() =>
        UiDocsTopic.Key("height").Heading("Its height").Paragraphs([
            "Four rows unless `Rows` says otherwise. `Rows(UiTextareaRows.Auto)` is `field-sizing: content`: the box "
            + "grows with each line, with no script, in the browsers that have it. `Resize` is the CSS `resize` the "
            + "reader drags with: `Vertical` when unset, `Horizontal`, `Both` or `None`, each a step of its own.",
        ]);

    private static UiDocsTopic Value() =>
        UiDocsTopic.Key("value").Heading("Its value").Paragraphs([
            "A textarea is bound like an input: `Bind(() => m.Notes)` writes with the reader's next action, and "
            + "`Value(notes).OnChange(…)` leaves the text with the page. `Label`, `Description`, `DescriptionTrailing` "
            + "and `Badge` draw the field around it, with the bound member's message, and a bound textarea is marked "
            + "invalid while that member holds one.",
        ]);
}
