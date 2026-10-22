namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class ColorPickerFormatExample : Component
{
    // One colour, written six ways: each picker writes it back in its own format.
    private const string Indigo = "rgb(99 102 241 / 0.5)";

    protected override Component? Render() =>
        Div.Class("grid max-w-xl gap-4 sm:grid-cols-2")[
            Ui.ColorPicker.Value(Indigo).Format(Ui.ColorPickerFormat.Hex),
            Ui.ColorPicker.Value(Indigo).Format(Ui.ColorPickerFormat.Hexa),
            Ui.ColorPicker.Value(Indigo).Format(Ui.ColorPickerFormat.Rgb),
            Ui.ColorPicker.Value(Indigo).Format(Ui.ColorPickerFormat.Rgba),
            Ui.ColorPicker.Value(Indigo).Format(Ui.ColorPickerFormat.Hsl),
            Ui.ColorPicker.Value(Indigo).Format(Ui.ColorPickerFormat.Hsla)
        ];
}
