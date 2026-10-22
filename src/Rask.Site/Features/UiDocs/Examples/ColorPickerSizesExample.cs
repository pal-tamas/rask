namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class ColorPickerSizesExample : Component
{
    protected override Component? Render() =>
        Div.Class("max-w-xs space-y-4")[
            Ui.ColorPicker.Value("#c0ffee"),
            Ui.ColorPicker.Value("#c0ffee").Sm,
            Ui.ColorPicker.Value("#c0ffee").Xs
        ];
}
