using Rask.Core.Routing;

namespace Rask.Site.Features.UiDocs;

/// <summary>Flux UI's checkbox and its group as C# components: each example with its own code, and its reference.</summary>
[Route("checkbox")]
[ParentRoute(typeof(UiDocsLayout))]
public sealed partial class UiCheckboxPage : Component
{
    /// <inheritdoc />
    protected override Component? HeadAssets => UiDocsCatalog.Checkbox.Meta();

    /// <inheritdoc />
    protected override Component? Render() =>
        UiComponentDoc
            .Entry(UiDocsCatalog.Checkbox)
            .Lead("One or several options from a set. On its own a checkbox binds a bool; in a group, the group binds the "
                  + "collection your model declares and each checkbox says what it holds while it is ticked.")
            .Api(UiApi.Checkbox)[
            UiDocsExample.Key("ui-checkbox").Heading("Checkbox").File("CheckboxExample.cs").Demo(CheckboxExample).Testid("ui-checkbox"),
            UiDocsExample.Key("ui-checkbox-group").Heading("Checkbox group").File("CheckboxGroupExample.cs").Demo(CheckboxGroupExample).Testid("ui-checkbox-group")
                .Blurb("Related checkboxes down a column, the group bound to the list of what is ticked."),
            UiDocsExample.Key("ui-checkbox-descriptions").Heading("With descriptions").File("CheckboxDescriptionsExample.cs").Demo(CheckboxDescriptionsExample).Testid("ui-checkbox-descriptions")
                .Blurb("A description sits under each checkbox's label."),
            UiDocsExample.Key("ui-checkbox-fieldset").Heading("Horizontal fieldset").File("CheckboxFieldsetExample.cs").Demo(CheckboxFieldsetExample).Testid("ui-checkbox-fieldset")
                .Blurb("Checkboxes across a row, under a legend, each bound to a bool of its own."),
            UiDocsExample.Key("ui-checkbox-all").Heading("Check-all").File("CheckboxAllExample.cs").Demo(CheckboxAllExample).Testid("ui-checkbox-all")
                .Blurb("One checkbox for the whole group: ticked when all are, a dash while some are."),
            UiDocsExample.Key("ui-checkbox-checked").Heading("Checked").File("CheckboxCheckedExample.cs").Demo(CheckboxCheckedExample).Testid("ui-checkbox-checked"),
            UiDocsExample.Key("ui-checkbox-disabled").Heading("Disabled").File("CheckboxDisabledExample.cs").Demo(CheckboxDisabledExample).Testid("ui-checkbox-disabled"),
            UiDocsExample.Key("ui-checkbox-cards").Heading("Checkbox cards").File("CheckboxCardsExample.cs").Demo(CheckboxCardsExample).Testid("ui-checkbox-cards")
                .Blurb("A bordered card is the checkbox: a press anywhere on it ticks it. max-sm:flex-col stacks the cards on a phone."),
            UiDocsExample.Key("ui-checkbox-vertical").Heading("Vertical cards").File("CheckboxVerticalCardsExample.cs").Demo(CheckboxVerticalCardsExample).Testid("ui-checkbox-vertical")
                .Blurb("flex-col on the group stacks the cards everywhere."),
            UiDocsExample.Key("ui-checkbox-card-icons").Heading("Cards with icons").File("CheckboxCardIconsExample.cs").Demo(CheckboxCardIconsExample).Testid("ui-checkbox-card-icons"),
            UiDocsExample.Key("ui-checkbox-custom").Heading("Custom card content").File("CheckboxCustomCardsExample.cs").Demo(CheckboxCustomCardsExample).Testid("ui-checkbox-custom")
                .Blurb("A card's children replace its label and description; Ui.CheckboxIndicator puts the box among them."),
            UiDocsExample.Key("ui-checkbox-pills").Heading("Pills").File("CheckboxPillsExample.cs").Demo(CheckboxPillsExample).Testid("ui-checkbox-pills")
                .Blurb("Compact, rounded checkboxes that look like tags: for filters and categories."),
            UiDocsExample.Key("ui-checkbox-buttons").Heading("Buttons").File("CheckboxButtonsExample.cs").Demo(CheckboxButtonsExample).Testid("ui-checkbox-buttons")
                .Blurb("Checkboxes drawn as a toolbar of buttons that stay pressed."),
            UiCheckboxBehaviour
        ];
}
