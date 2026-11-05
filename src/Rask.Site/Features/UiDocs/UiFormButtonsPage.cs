using Rask.Core.Routing;

namespace Rask.Site.Features.UiDocs;

/// <summary>
///     The kit's form footer — not a Flux component, an addition the owner approved — its example with its code, and its
///     reference.
/// </summary>
[Route("form-buttons")]
[ParentRoute(typeof(UiDocsLayout))]
public sealed partial class UiFormButtonsPage : Component
{
    /// <inheritdoc />
    protected override Component? HeadAssets => UiDocsCatalog.FormButtons.Meta();

    /// <inheritdoc />
    protected override Component? Render() =>
        UiComponentDoc
            .Entry(UiDocsCatalog.FormButtons)
            .Lead("A form's footer in one line, as Flux draws one: Delete at the start, then Cancel back to the list and "
                  + "Save. Not one of Flux UI's components: the kit's own, made of its button and spacer, worded from the "
                  + "RaskStrings catalog — a Hungarian reader reads Törlés, Mégse and Mentés.")
            .Api(UiApi.FormButtons)[
            UiDocsExample.Key("ui-form-buttons").Heading("A form's footer").File("FormButtonsExample.cs").Demo(FormButtonsExample).Testid("ui-form-buttons")
                .Blurb("Back(list) is where Cancel goes: a link, so a form's ConfirmLeave() asks first when something was "
                       + "typed. Remove(delete, heading, question) puts Delete at the start, which asks in the layout's "
                       + "Ui.Confirm and deletes only on yes. Save submits the form it stands in."),
            UiDocsExample.Key("ui-form-buttons-words").Heading("Your own words").File("FormButtonsWordsExample.cs").Demo(FormButtonsWordsExample).Testid("ui-form-buttons-words")
                .Blurb("A new record is created, not saved: Save(\"Create\") and Cancel(\"Back\") name the two buttons. "
                       + "Unset, they are the catalog's Save and Cancel — Mentés and Mégse for a Hungarian reader.")
        ];
}
