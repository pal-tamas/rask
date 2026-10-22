namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class ColorPickerSwatchesExample : Component
{
    protected override Component? Render() =>
        Div.Class("max-w-xs space-y-4")[
            Ui.ColorPicker.Value("#ef4444").Swatches(["#ef4444", "#22c55e", "#3b82f6", "#f59e0b", "#a855f7"]),
            // With a label for each swatch, which assistive tech reads.
            Ui.ColorPicker.Value("#ef4444").Swatches([("#ef4444", "Red"), ("#22c55e", "Green"), ("#3b82f6", "Blue")])
        ];
}
