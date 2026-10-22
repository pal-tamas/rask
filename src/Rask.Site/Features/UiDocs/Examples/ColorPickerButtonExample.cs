namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class ColorPickerButtonExample : Component
{
    protected override Component? Render() =>
        Ui.ColorPicker.Value("#ca1ebb").Type(Ui.ColorPickerType.Button);
}
