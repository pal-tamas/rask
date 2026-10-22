namespace Rask.Site.Features.UiDocs;

/// <summary>How a checkbox behaves: the group holds the value, the check-all, real inputs, and validation.</summary>
internal sealed partial class UiCheckboxBehaviour : Component
{
    /// <inheritdoc />
    protected override Component? Render() =>
        UiDocsBehaviour[
            Group(),
            All(),
            RealInputs(),
            Validation()
        ];

    private static UiDocsTopic Group() =>
        UiDocsTopic.Key("group").Heading("The group holds the value").Paragraphs([
            "On its own a checkbox binds a `bool`, or a `bool?` whose `null` draws the dash: "
            + "`Ui.Checkbox.Bind(() => m.Agreed)`, or `Checked(on).OnChange(v => on = v)` for one the page owns. A "
            + "checkbox inside a `Ui.CheckboxGroup` binds nothing itself: the group binds the collection (a `List<T>`, "
            + "a `HashSet<T>`, an array) and each checkbox's `Value` is what the collection holds while it is ticked, "
            + "written in the order the checkboxes are written. The group's controlled opening is `Values`.",
            "`Cards`, `Pills` and `Buttons` draw the same checkboxes differently. A choice's `Label`, `Description` and "
            + "`Icon` are drawn on the card, the pill or the button, and a card's children replace them.",
        ]);

    private static UiDocsTopic All() =>
        UiDocsTopic.Key("all").Heading("The check-all").Paragraphs([
            "`Ui.CheckboxAll` sits among a group's checkboxes, anywhere in its markup. It is ticked when all of them "
            + "are, shows a dash (`data-indeterminate`, as Flux marks it) while some are, and a press ticks them all "
            + "unless they all are already. A disabled checkbox keeps what it has. `Ui.Checkbox.Indeterminate()` draws "
            + "the same dash on a checkbox of your own.",
        ]);

    private static UiDocsTopic RealInputs() =>
        UiDocsTopic.Key("inputs").Heading("Real inputs, no script").Paragraphs([
            "Every root (`data-ui-checkbox`, and `data-ui-checkbox-cards`, `-pills` or `-buttons`) is a `<label>` around "
            + "an `<input type=\"checkbox\">` that is out of sight, never `hidden`. The tick reads the input's own "
            + "`:checked`, so the space bar, a click on the label and the form post are the browser's, however the "
            + "checkbox is drawn. `Name` is what it is posted under, and `Attributes((\"aria-label\", \"Select row\"))` "
            + "forwards anything else to the input.",
        ]);

    private static UiDocsTopic Validation() =>
        UiDocsTopic.Key("validation").Heading("Validation").Paragraphs([
            "A bound checkbox or group is invalid on its own while its form holds a message for the member: "
            + "`aria-invalid`, `data-invalid`, and `aria-describedby` naming the field's `Ui.Error`, which `Label` draws "
            + "with it. `Invalid()` says so by hand. Flux's pages show no invalid checkbox to measure, so the unticked "
            + "box takes the red border Flux's input has.",
        ]);
}
