namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class ColorPickerFormExample : Component
{
    private readonly Brand _brand = new();
    private string _saved = "nothing yet";

    protected override Component? Render() =>
        Form.Model(_brand).OnSubmit(_ => { _saved = _brand.Accent; }).Class("flex max-w-xs flex-col items-start gap-3")[
            Ui.ColorPicker.Bind(() => _brand.Accent).Label("Accent").Description("Saved with the form.").Id("ui-color-picker-accent"),
            Ui.Button.Primary.Type(Ui.ButtonType.Submit)["Save"],
            Ui.Text.Data("testid", "ui-color-picker-saved")[$"Saved: {_saved}."]
        ];

    public sealed class Brand
    {
        public string Accent { get; set; } = "#6366f1";
    }
}
