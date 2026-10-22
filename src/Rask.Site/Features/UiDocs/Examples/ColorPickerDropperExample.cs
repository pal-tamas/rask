namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class ColorPickerDropperExample : Component
{
    protected override Component? Render() =>
        Div.Class("max-w-xs")[
            Ui.ColorPicker.Value("#dec0de").Dropper()
        ];
}
