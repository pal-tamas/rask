namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class ColorPickerLayoutExample : Component
{
    protected override Component? Render() =>
        Div.Class("max-w-xs")[
            Ui.ColorPicker.Value("#facade")[
                Div.Class("flex flex-col gap-3")[
                    Ui.ColorPickerInput.Placeholder("#000000"),
                    Ui.Separator,
                    Ui.ColorPickerArea,
                    Ui.ColorPickerSlider.Channel(Ui.ColorPickerChannel.Hue)
                ]
            ]
        ];
}
