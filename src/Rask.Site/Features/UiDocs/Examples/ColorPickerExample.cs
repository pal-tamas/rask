namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class ColorPickerExample : Component
{
    private string _theme = "#bada55";

    protected override Component? Render() =>
        Div.Class("max-w-xs space-y-3")[
            Ui.ColorPicker.Value(_theme).OnChange(color => { _theme = color; }).Id("ui-color-picker-intro"),
            Ui.Text.Data("testid", "ui-color-picker-state")[$"Theme: {(_theme.Length == 0 ? "none" : _theme)}."]
        ];
}
