namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class ColorPickerNoSwatchesExample : Component
{
    protected override Component? Render() =>
        Div.Class("max-w-xs")[
            Ui.ColorPicker.Value("#ef4444").Swatches([])
        ];
}
