namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class ColorPickerClearableExample : Component
{
    private string _color = "#deface";

    protected override Component? Render() =>
        Div.Class("max-w-xs space-y-3")[
            Ui.ColorPicker.Value(_color).OnChange(color => { _color = color; }).Clearable(),
            Ui.Text.Data("testid", "ui-color-picker-cleared")[$"Color: {(_color.Length == 0 ? "none" : _color)}."]
        ];
}
