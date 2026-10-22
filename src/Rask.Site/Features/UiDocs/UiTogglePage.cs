using Rask.Core.Routing;

namespace Rask.Site.Features.UiDocs;

/// <summary>Flux UI's toggle as a C# component: each example with its own code, and its reference.</summary>
[Route("toggle")]
[ParentRoute(typeof(UiDocsLayout))]
public sealed partial class UiTogglePage : Component
{
    /// <inheritdoc />
    protected override Component? HeadAssets => UiDocsCatalog.Toggle.Meta();

    /// <inheritdoc />
    protected override Component? Render() =>
        UiComponentDoc
            .Entry(UiDocsCatalog.Toggle)
            .Lead("A setting turned on or off from a compact, button-shaped control. Use a toggle for icon-sized settings "
                  + "and modes, and a switch when the setting needs a visible label.")
            .Api(UiApi.Toggle)[
            UiDocsExample.Key("ui-toggle-basic").Heading("Toggle").File("ToggleExample.cs").Demo(ToggleExample).Testid("ui-toggle-basic")
                .Blurb("Nobody listens to this one, so the browser turns it and the page is sent nothing."),
            UiDocsExample.Key("ui-toggle-saving").Heading("Saving changes").File("ToggleSavingExample.cs").Demo(ToggleSavingExample).Testid("ui-toggle-saving")
                .Blurb("Bound, with OnChange to save: the member is written first, and the toggle shows a spinner while the save runs."),
            UiDocsExample.Key("ui-toggle-sizes").Heading("Sizes").File("ToggleSizesExample.cs").Demo(ToggleSizesExample).Testid("ui-toggle-sizes"),
            UiDocsExample.Key("ui-toggle-variants").Heading("Variants").File("ToggleVariantsExample.cs").Demo(ToggleVariantsExample).Testid("ui-toggle-variants"),
            UiDocsExample.Key("ui-toggle-color").Heading("Color").File("ToggleColorExample.cs").Demo(ToggleColorExample).Testid("ui-toggle-color")
                .Blurb("The colour of the icon when on. Off, it stays neutral."),
            UiDocsExample.Key("ui-toggle-label").Heading("With label").File("ToggleLabelExample.cs").Demo(ToggleLabelExample).Testid("ui-toggle-label"),
            UiDocsExample.Key("ui-toggle-stateful-icon").Heading("Stateful icon").File("ToggleStatefulIconExample.cs").Demo(ToggleStatefulIconExample).Testid("ui-toggle-stateful-icon")
                .Blurb("A different icon for each state, kept by the page."),
            UiDocsExample.Key("ui-toggle-custom-icons").Heading("Custom icons").File("ToggleCustomIconsExample.cs").Demo(ToggleCustomIconsExample).Testid("ui-toggle-custom-icons")
                .Blurb("Icons of your own, each shown for its state with group-data-checked:."),
            UiDocsExample.Key("ui-toggle-stateful-label").Heading("Stateful label").File("ToggleStatefulLabelExample.cs").Demo(ToggleStatefulLabelExample).Testid("ui-toggle-stateful-label"),
            UiDocsExample.Key("ui-toggle-disabled").Heading("Disabled").File("ToggleDisabledExample.cs").Demo(ToggleDisabledExample).Testid("ui-toggle-disabled")
                .Blurb("Dimmed, and out of the tab order."),
            UiToggleBehaviour
        ];
}
