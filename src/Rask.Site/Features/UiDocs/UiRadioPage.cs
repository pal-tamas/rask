using Rask.Core.Routing;

namespace Rask.Site.Features.UiDocs;

/// <summary>Flux UI's radio and its group as C# components: each example with its own code, and its reference.</summary>
[Route("radio")]
[ParentRoute(typeof(UiDocsLayout))]
public sealed partial class UiRadioPage : Component
{
    /// <inheritdoc />
    protected override Component? HeadAssets => UiDocsCatalog.Radio.Meta();

    /// <inheritdoc />
    protected override Component? Render() =>
        UiComponentDoc
            .Entry(UiDocsCatalog.Radio)
            .Lead("One option from a set that excludes the others. The group binds one value, of any type, and each radio "
                  + "says what the value becomes when it is chosen.")
            .Api(UiApi.Radio)[
            UiDocsExample.Key("ui-radio").Heading("Radio").File("RadioExample.cs").Demo(RadioExample).Testid("ui-radio"),
            UiDocsExample.Key("ui-radio-descriptions").Heading("With descriptions").File("RadioDescriptionsExample.cs").Demo(RadioDescriptionsExample).Testid("ui-radio-descriptions")
                .Blurb("A description sits under each radio's label."),
            UiDocsExample.Key("ui-radio-fieldset").Heading("Within fieldset").File("RadioFieldsetExample.cs").Demo(RadioFieldsetExample).Testid("ui-radio-fieldset")
                .Blurb("The group inside a fieldset, whose legend names it."),
            UiDocsExample.Key("ui-radio-segmented").Heading("Segmented").File("RadioSegmentedExample.cs").Demo(RadioSegmentedExample).Testid("ui-radio-segmented")
                .Blurb("A more compact row of the same radios."),
            UiDocsExample.Key("ui-radio-segmented-sm").Heading("Small segmented").File("RadioSegmentedSmallExample.cs").Demo(RadioSegmentedSmallExample).Testid("ui-radio-segmented-sm"),
            UiDocsExample.Key("ui-radio-segmented-icons").Heading("Segmented with icons").File("RadioSegmentedIconsExample.cs").Demo(RadioSegmentedIconsExample).Testid("ui-radio-segmented-icons"),
            UiDocsExample.Key("ui-radio-cards").Heading("Radio cards").File("RadioCardsExample.cs").Demo(RadioCardsExample).Testid("ui-radio-cards")
                .Blurb("A bordered card is the radio: a press anywhere on it chooses it. max-sm:flex-col stacks the cards on a phone."),
            UiDocsExample.Key("ui-radio-vertical").Heading("Vertical cards").File("RadioVerticalCardsExample.cs").Demo(RadioVerticalCardsExample).Testid("ui-radio-vertical"),
            UiDocsExample.Key("ui-radio-card-icons").Heading("Cards with icons").File("RadioCardIconsExample.cs").Demo(RadioCardIconsExample).Testid("ui-radio-card-icons"),
            UiDocsExample.Key("ui-radio-bare").Heading("Cards without indicators").File("RadioBareCardsExample.cs").Demo(RadioBareCardsExample).Testid("ui-radio-bare")
                .Blurb("Indicator(false) leaves the dot out, for a cleaner card."),
            UiDocsExample.Key("ui-radio-custom").Heading("Custom card content").File("RadioCustomCardsExample.cs").Demo(RadioCustomCardsExample).Testid("ui-radio-custom")
                .Blurb("A card's children replace its label and description; Ui.RadioIndicator puts the dot among them."),
            UiDocsExample.Key("ui-radio-pills").Heading("Pills").File("RadioPillsExample.cs").Demo(RadioPillsExample).Testid("ui-radio-pills"),
            UiDocsExample.Key("ui-radio-buttons").Heading("Buttons").File("RadioButtonsExample.cs").Demo(RadioButtonsExample).Testid("ui-radio-buttons")
                .Blurb("Radios drawn as a toolbar. A radio's children are its words, as a button's are."),
            UiRadioBehaviour
        ];
}
