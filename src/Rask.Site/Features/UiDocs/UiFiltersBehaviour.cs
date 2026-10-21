namespace Rask.Site.Features.UiDocs;

/// <summary>How a set of filters behaves: the address it keeps them in, when a filter applies, its badges and its panel.</summary>
internal sealed partial class UiFiltersBehaviour : Component
{
    /// <inheritdoc />
    protected override Component? Render() =>
        UiDocsBehaviour[
            Address(),
            Applying(),
            Badges(),
            Panel()
        ];

    private static UiDocsTopic Address() =>
        UiDocsTopic.Key("address").Heading("Kept in the address").Paragraphs([
            "Each control is bound to a page member with `[QueryParam]`, as it would be in a form, and the set binds it to "
            + "the address instead: a pick moves the query, the router writes the member, and the page hears `OnUpdated` "
            + "as for any move of its query. A filtered list can be linked to, reloaded and stepped back through.",
            "The key is the attribute's name, or the member's own. A member without one fails as the page renders, "
            + "naming the declaration to write. A range is one key, `period=2026-10-01..2026-10-09`, because "
            + "`UiDateRange` reads and writes itself as text: `[QueryParam] public UiDateRange? Period` works on any page.",
            "A move replaces the history entry, so Back leaves the list rather than undoing each pick.",
        ]);

    private static UiDocsTopic Applying() =>
        UiDocsTopic.Key("applying").Heading("Applied as chosen").Paragraphs([
            "There is no Apply button. A pick in a select, a date picker or a box applies at once; a text field once "
            + "typing pauses for 150 ms. A `Debounce` or `Blur` the call site writes on a field is kept.",
        ]);

    private static Component Badges() =>
        UiDocsTopic.Key("badges").Heading("Badges, and what counts").Paragraphs([
            "`filters.Badges` is a badge for each filter in force, in the words its control shows — \"Driver: Ada "
            + "Lovelace\", a range as the picker writes it or the name of the preset that made it — each with a cross "
            + "that takes that one key out. From two on, Clear all takes every filter's key out in one move and leaves "
            + "the rest of the address alone. `filters.Active` counts them, and the Filters button shows the count.",
        ])[
            UiDocsKeys.Column("Control").Rows([
                ("`Ui.Select`, `Ui.RadioGroup`", "The words of the option or radio it holds."),
                ("`Ui.DatePicker`", "What its button says: the day, the two days, or the preset's name."),
                ("`Ui.Input`", "What was typed, trimmed. Blank is no filter."),
                ("`Ui.Checkbox`, `Ui.Switch`", "Its label alone, while it is on."),
                ("`Ui.Filter`", "Its `Label` and its `Shown`, while `Shown` is set."),
            ])
        ];

    private static UiDocsTopic Panel() =>
        UiDocsTopic.Key("panel").Heading("A lazy panel").Paragraphs([
            "The fields are in a flyout that is a lazy `Ui.Modal`: closed, the page sends the dialog alone, and opening "
            + "it is answered with one frame that carries the fields, so a reader who never filters is sent none of "
            + "them. `OnOpen` runs as it opens, where a page loads the options a field offers.",
            "`Ui.Modal.Lazy()` is a step of its own, for any modal that holds what most readers never open; Flux's modal "
            + "has no such switch. It is off by default: a modal that must open with no server at all, such as "
            + "`Ui.ConfirmLeave` or a ⌘K palette, has to hold its content before anyone asks.",
        ]);
}
