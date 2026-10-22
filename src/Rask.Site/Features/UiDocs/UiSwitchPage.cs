using Rask.Core.Routing;

namespace Rask.Site.Features.UiDocs;

/// <summary>Flux UI's switch as a C# component: each example with its own code, and its reference.</summary>
[Route("switch")]
[ParentRoute(typeof(UiDocsLayout))]
public sealed partial class UiSwitchPage : Component
{
    /// <inheritdoc />
    protected override Component? HeadAssets => UiDocsCatalog.Switch.Meta();

    /// <inheritdoc />
    protected override Component? Render() =>
        UiComponentDoc
            .Entry(UiDocsCatalog.Switch)
            .Lead("A setting turned on or off. Use a switch for a setting that saves as it is flipped, outside a form, and "
                  + "a checkbox for a fact a form submits later.")
            .Api(UiApi.Switch)[
            UiDocsExample.Key("ui-switch").Heading("Switch").File("SwitchExample.cs").Demo(SwitchExample).Testid("ui-switch")
                .Blurb("In an inline field, bound to a bool and written as it is flipped."),
            UiDocsExample.Key("ui-switch-fieldset").Heading("Fieldset").File("SwitchFieldsetExample.cs").Demo(SwitchFieldsetExample).Testid("ui-switch-fieldset")
                .Blurb("Related switches in a fieldset, each with its own label and description."),
            UiDocsExample.Key("ui-switch-left").Heading("Left align").File("SwitchLeftExample.cs").Demo(SwitchLeftExample).Testid("ui-switch-left")
                .Blurb("Left puts the switch before its label, for a more compact column."),
            UiSwitchBehaviour
        ];
}
