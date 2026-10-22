namespace Rask.Site.Features.UiDocs;

/// <summary>How a field behaves: the label is a real one, the error reads the form, and what Flux has that it has not.</summary>
internal sealed partial class UiFieldBehaviour : Component
{
    /// <inheritdoc />
    protected override Component? Render() =>
        UiDocsBehaviour[
            RealLabel(),
            Error(),
            Order()
        ];

    private static UiDocsTopic RealLabel() =>
        UiDocsTopic.Key("label").Heading("The label is a real label").Paragraphs([
            "`Ui.Label` is a `<label for>`. Inside a field it points at the field's control (a kit control by the id it "
            + "derives, a plain element by its `Id`), so a click on the label focuses the control with no script. Outside "
            + "a field, `For(id)` names the control. Flux draws its parts as custom elements its script wires together; "
            + "the kit ships none, so each part is the native element that already behaves that way: `<label>`, "
            + "`<fieldset>`, `<legend>`.",
            "`Ui.Field.Inline` puts the label beside the control, for a checkbox or a switch; the default stacks them.",
        ]);

    private static UiDocsTopic Error() =>
        UiDocsTopic.Key("error").Heading("The error reads the form").Paragraphs([
            "Where Flux's `name` looks a key up in Laravel's error bag, a Rask field is a member of a model, so `Ui.Error` "
            + "shows the first message the form holds for that member: bare inside a field (the control's own `Bind`), "
            + "`For(() => model.Email)` anywhere, or `Name(\"Email\")` for a member of the form's model. `Message(\"…\")` "
            + "shows your own words whatever the form says, and `Icon(false)` leaves the triangle out.",
            "It is always in the page and hidden while there is nothing to say, because it is a `role=\"alert\"` live "
            + "region: a screen reader announces a message only when it arrives in a region it already knows. A bound "
            + "control is invalid on its own while its member holds a message: `aria-invalid`, `data-invalid` and the red "
            + "border, with `aria-describedby` naming the error first and the description after it.",
            "Flux's `bag` and `deep` have no counterpart: a form has one edit context, and a nested member is named by "
            + "its expression rather than by a dotted path.",
        ]);

    private static UiDocsTopic Order() =>
        UiDocsTopic.Key("order").Heading("Where the description goes").Paragraphs([
            "A `Ui.Description` before the control sits under the label; after the control it sits under the control. "
            + "`Label`, `Description` and `DescriptionTrailing` on a control draw the same field around it, with its "
            + "`Ui.Error`, and `ShowValidation(false)` leaves the message to a `Ui.Error` placed somewhere else. Markers "
            + "follow Flux's: `data-ui-field`, `data-ui-label`, `data-ui-description`, `data-ui-error`, "
            + "`data-ui-fieldset`, `data-ui-legend`.",
        ]);
}
