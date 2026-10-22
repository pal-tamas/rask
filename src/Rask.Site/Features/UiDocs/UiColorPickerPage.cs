using Rask.Core.Routing;

namespace Rask.Site.Features.UiDocs;

/// <summary>Flux UI's color picker and its parts as C# components: each example with its own code, and its reference.</summary>
[Route("color-picker")]
[ParentRoute(typeof(UiDocsLayout))]
public sealed partial class UiColorPickerPage : Component
{
    /// <inheritdoc />
    protected override Component? HeadAssets => UiDocsCatalog.ColorPicker.Meta();

    /// <inheritdoc />
    protected override Component? Render() =>
        UiComponentDoc
            .Entry(UiDocsCatalog.ColorPicker)
            .Lead("A colour picked from a palette, dragged out of a saturation and brightness area, or typed, for theme "
                  + "settings and brand colours. Its value is CSS text in the format it is set to.")
            .Api(UiApi.ColorPicker)[
            UiDocsExample.Key("ui-color-picker").Heading("Color picker").File("ColorPickerExample.cs").Demo(ColorPickerExample).Testid("ui-color-picker")
                .Blurb("The swatch button opens the popover; a typed colour is taken on Enter or on leaving the input."),
            UiDocsExample.Key("ui-color-picker-format").Heading("Format").File("ColorPickerFormatExample.cs").Demo(ColorPickerFormatExample).Testid("ui-color-picker-format")
                .Blurb("One colour written six ways. An alpha format adds an opacity slider to the popover."),
            UiDocsExample.Key("ui-color-picker-sizes").Heading("Sizes").File("ColorPickerSizesExample.cs").Demo(ColorPickerSizesExample).Testid("ui-color-picker-sizes"),
            UiDocsExample.Key("ui-color-picker-button").Heading("Button trigger").File("ColorPickerButtonExample.cs").Demo(ColorPickerButtonExample).Testid("ui-color-picker-button")
                .Blurb("One button that shows the colour, for a toolbar or a palette where nothing is typed."),
            UiDocsExample.Key("ui-color-picker-swatches").Heading("Custom swatches").File("ColorPickerSwatchesExample.cs").Demo(ColorPickerSwatchesExample).Testid("ui-color-picker-swatches")
                .Blurb("Your palette in place of the 24 Tailwind colours: strings, or (value, label) pairs."),
            UiDocsExample.Key("ui-color-picker-bare").Heading("No swatches").File("ColorPickerNoSwatchesExample.cs").Demo(ColorPickerNoSwatchesExample).Testid("ui-color-picker-bare")
                .Blurb("An empty list hides the palette, Flux's :swatches=\"false\": the area, the hue slider and the input."),
            UiDocsExample.Key("ui-color-picker-layout").Heading("Custom layout").File("ColorPickerLayoutExample.cs").Demo(ColorPickerLayoutExample).Testid("ui-color-picker-layout")
                .Blurb("Children compose the popover from its parts, in any order."),
            UiDocsExample.Key("ui-color-picker-dropper").Heading("Dropper").File("ColorPickerDropperExample.cs").Demo(ColorPickerDropperExample).Testid("ui-color-picker-dropper")
                .Blurb("A button that picks a colour from anywhere on the screen, there only in a browser with an EyeDropper."),
            UiDocsExample.Key("ui-color-picker-clearable").Heading("Clearable").File("ColorPickerClearableExample.cs").Demo(ColorPickerClearableExample).Testid("ui-color-picker-clearable"),
            UiDocsExample.Key("ui-color-picker-copyable").Heading("Copyable").File("ColorPickerCopyableExample.cs").Demo(ColorPickerCopyableExample).Testid("ui-color-picker-copyable")
                .Blurb("One click copies the colour as it is written."),
            UiDocsExample.Key("ui-color-picker-disabled").Heading("Disabled").File("ColorPickerDisabledExample.cs").Demo(ColorPickerDisabledExample).Testid("ui-color-picker-disabled"),
            UiDocsExample.Key("ui-color-picker-form").Heading("In a form").File("ColorPickerFormExample.cs").Demo(ColorPickerFormExample).Testid("ui-color-picker-form")
                .Blurb("Bound in a form, the colour travels with the save, and text that is no colour holds the save back."),
            UiColorPickerBehaviour
        ];
}
