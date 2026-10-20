using Rask.Core.Routing;

namespace Rask.Site.Features.UiDocs;

/// <summary>Flux UI's button and button group as a C# component: each example with its own code, and its reference.</summary>
[Route("button")]
[ParentRoute(typeof(UiDocsLayout))]
public sealed partial class UiButtonPage : Component
{
    /// <inheritdoc />
    protected override Component? HeadAssets => UiDocsCatalog.Button.Meta();

    /// <inheritdoc />
    protected override Component? Render() =>
        UiComponentDoc
            .Entry(UiDocsCatalog.Button)
            .Lead("A button is a <button>, or an <a> given Href. Ui.Button.Primary.Sm.Blue is a variant, a size "
                  + "and a colour, each a step; an icon is a step too, so the button pads itself around it.")
            .Api(UiApi.Button)[
            UiDocsExample.Key("ui-button-intro").Heading("Button").File("ButtonExample.cs").Demo(ButtonExample).Testid("ui-button-intro")
                .Blurb("Flux's default is the outline: a white surface, a hairline border, a soft shadow."),
            UiDocsExample.Key("ui-button-variants").Heading("Variants").File("ButtonVariantsExample.cs").Demo(ButtonVariantsExample).Testid("ui-button-variants")
                .Blurb("Six ways to draw it. Primary is for the one action a view is about — mostly a form's submit."),
            UiDocsExample.Key("ui-button-colors").Heading("Colors").File("ButtonColorsExample.cs").Demo(ButtonColorsExample).Testid("ui-button-colors")
                .Blurb("Any Tailwind hue, as a step. It recolours every variant but danger."),
            UiDocsExample.Key("ui-button-outline-colors").Heading("Outline colors").File("ButtonOutlineColorsExample.cs").Demo(ButtonOutlineColorsExample).Testid("ui-button-outline-colors")
                .Blurb("A coloured outline keeps the white surface and mixes the hue into its label, border and "
                       + "hover."),
            UiDocsExample.Key("ui-button-sizes").Heading("Sizes").File("ButtonSizesExample.cs").Demo(ButtonSizesExample).Testid("ui-button-sizes")
                .Blurb("40px, 32px and 24px tall."),
            UiDocsExample.Key("ui-button-icons").Heading("Icons").File("ButtonIconsExample.cs").Demo(ButtonIconsExample).Testid("ui-button-icons")
                .Blurb("An icon is a step, so the button pads itself around it. With no label it is a square — name "
                       + "it."),
            UiDocsExample.Key("ui-button-loading").Heading("Loading").File("ButtonLoadingExample.cs").Demo(ButtonLoadingExample).Testid("ui-button-loading")
                .Blurb("Nothing to set. A button whose handler is still running after 200 ms shows a spinner at the "
                       + "same width, tells a screen reader it is busy and drops a second press. Loading(false) opts a "
                       + "stepper out, so its presses queue."),
            UiDocsExample.Key("ui-button-full-width").Heading("Full width").File("ButtonFullWidthExample.cs").Demo(ButtonFullWidthExample).Testid("ui-button-full-width")
                .Blurb("A width class is all it takes."),
            UiDocsExample.Key("ui-button-groups").Heading("Button groups").File("ButtonGroupsExample.cs").Demo(ButtonGroupsExample).Testid("ui-button-groups")
                .Blurb("Related buttons fused into one control: one border between each pair, corners at the two "
                       + "ends."),
            UiDocsExample.Key("ui-button-route").Heading("As a link").File("ButtonLinkExample.cs").Demo(ButtonLinkExample).Testid("ui-button-route")
                .Blurb("Given an Href it is an <a>. A generated route navigates inside the app without reloading it; "
                       + "a plain string is a link the browser follows."),
            UiDocsExample.Key("ui-button-square").Heading("Square").File("ButtonSquareExample.cs").Demo(ButtonSquareExample).Testid("ui-button-square")
                .Blurb("As wide as it is tall. Automatic for a button that shows only an icon."),
            UiDocsExample.Key("ui-button-inset").Heading("Inset").File("ButtonInsetExample.cs").Demo(ButtonInsetExample).Testid("ui-button-inset")
                .Blurb("A ghost button's padding is invisible, so it sits further in than it looks. Inset pulls it "
                       + "back out, in line with the heading.")
        ];
}
