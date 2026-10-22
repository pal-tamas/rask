namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class ColorPickerDisabledExample : Component
{
    protected override Component? Render() =>
        Div.Class("max-w-xs")[
            Ui.ColorPicker.Value("#beefed").Disabled()
        ];
}
