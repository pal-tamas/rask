using Rask.Core.Routing;

namespace Rask.Site.Features.UiDocs;

/// <summary>
///     The kit's tabs for a long form — not a Flux component, an addition the owner approved — its example with its code,
///     and its reference.
/// </summary>
[Route("form-tabs")]
[ParentRoute(typeof(UiDocsLayout))]
public sealed partial class UiFormTabsPage : Component
{
    /// <inheritdoc />
    protected override Component? HeadAssets => UiDocsCatalog.FormTabs.Meta();

    /// <inheritdoc />
    protected override Component? Render() =>
        UiComponentDoc
            .Entry(UiDocsCatalog.FormTabs)
            .Lead("A long form split into tabs, each saying in a red badge how many of its fields need correcting, and a "
                  + "refused save opening the first tab that holds one. Not one of Flux UI's components: the kit's own, "
                  + "made of Flux's tabs and badge, reading the form it stands in — the page hands it nothing.")
            .Api(UiApi.FormTabs)[
            UiDocsExample.Key("ui-form-tabs").Heading("A long form's tabs").File("FormTabsExample.cs").Demo(FormTabsExample).Testid("ui-form-tabs")
                .Blurb("Press Save with Email, Street and City empty: Details says 1 and Address 2, and Details opens. Fill "
                       + "Email and press Save again: its count goes and Address opens — a correction counts once it reaches the "
                       + "form, with the next action, or as it is typed in a .Live() field. Holds(() => m.Address) holds every field on the address; a tab "
                       + "that holds nothing, like Notes, never shows a count. Every panel stays in the form, so what was "
                       + "typed on a hidden tab is saved with the rest.")
        ];
}
