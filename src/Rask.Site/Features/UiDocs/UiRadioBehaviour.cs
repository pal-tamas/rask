namespace Rask.Site.Features.UiDocs;

/// <summary>How a radio group behaves: one value of any type, the keys, and the names it posts under.</summary>
internal sealed partial class UiRadioBehaviour : Component
{
    /// <inheritdoc />
    protected override Component? Render() =>
        UiDocsBehaviour[
            Value(),
            Keyboard(),
            Names()
        ];

    private static UiDocsTopic Value() =>
        UiDocsTopic.Key("value").Heading("One value, of any type").Paragraphs([
            "The group binds one value: `Ui.RadioGroup.Bind(() => m.Plan)` over a string, an enum or a number, or "
            + "`Value(plan).OnChange(…)` for one the page owns. Each `Ui.Radio`'s `Value` is what the member becomes "
            + "when that radio is chosen, and `Ui.Radio.Checked()` chooses one only while the group holds no value. "
            + "`Segmented` (with `Sm`), `Cards` (with `Indicator(false)`), `Pills` and `Buttons` draw the same radios.",
        ]);

    private static Component Keyboard() =>
        UiDocsTopic.Key("keyboard").Heading("The keyboard").Paragraphs([
            "Each radio is a `<label>` around a real `<input type=\"radio\">` that shares the group's name, so the keys "
            + "are the browser's in every variant, and so are a click on the label and the form post.",
        ])[
            UiDocsKeys.Column("Key").Rows([
                ("ArrowDown, ArrowRight", "Moves to the next radio and chooses it, wrapping from the last to the first."),
                ("ArrowUp, ArrowLeft", "Moves to the previous radio and chooses it, wrapping from the first to the last."),
                ("Tab", "Enters the group at the chosen radio, and leaves it."),
                ("Space", "Chooses the focused radio."),
            ])
        ];

    private static UiDocsTopic Names() =>
        UiDocsTopic.Key("names").Heading("What it posts").Paragraphs([
            "A radio group names its radios after its own id unless `Name` says otherwise, and a `Name` on one radio "
            + "replaces the group's for that radio. A bound group is invalid on its own while its form holds a message "
            + "for the member, and `Invalid()` says so by hand.",
        ]);
}
