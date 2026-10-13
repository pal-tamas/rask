namespace Rask.Site.Features.UiKit;

/// <summary>
///     Flux UI's color picker page, example by example: https://fluxui.dev/components/color-picker
/// </summary>
public sealed partial class UiKitColorPickerDemo : Component
{
    private readonly Brand _brand = new();
    private string _theme = "#bada55";
    private string _tint = "rgb(99 102 241 / 0.5)";
    private string _palette = "#ef4444";
    private string _cleared = "#deface";
    private string _saved = "nothing yet";

    /// <inheritdoc />
    protected override Component? Render() =>
        Div.Key("Color picker").Class("mb-8")[
            H2.Class("text-lg font-semibold tracking-tight")["Color picker"],
            P.Class("mt-1 mb-3 text-sm text-ui-muted")[
                "Pick a colour from a palette, drag it out of the saturation and brightness area, or type it in any CSS "
                + "form. Format says how it is written back, Type(Button) trades the input for one button, Swatches "
                + "replaces the palette, and children compose the popover from its parts. On a phone the popover is a "
                + "sheet that keeps what you choose until you press Select."
            ],
            Div.Data(Testid("ui-color-picker")).Class("grid max-w-3xl items-start gap-6 sm:grid-cols-3")[
                Example("intro", Ui.ColorPicker.Value(_theme).OnChange(color => _theme = color).Id("ui-color-picker-intro")),
                Example("format", Ui.ColorPicker.Value(_tint).OnChange(color => _tint = color).Format(Ui.ColorPickerFormat.Rgba).Label("rgba")),
                Example("button", Ui.ColorPicker.Value("#ca1ebb").Type(Ui.ColorPickerType.Button)),
                Example("sm", Ui.ColorPicker.Value("#c0ffee").Sm),
                Example("xs", Ui.ColorPicker.Value("#c0ffee").Xs),
                Example("swatches", Ui.ColorPicker.Value(_palette).OnChange(color => _palette = color).Swatches(
                [
                    ("#ef4444", "Red"), ("#f97316", "Orange"), ("#f59e0b", "Amber"), ("#22c55e", "Green"),
                    ("#14b8a6", "Teal"), ("#3b82f6", "Blue"), ("#a855f7", "Purple"), ("#ec4899", "Pink"),
                ])),
                Example("bare", Ui.ColorPicker.Value("#ef4444").Swatches([])),
                Example("layout", Ui.ColorPicker.Value("#facade")[
                    Div.Class("flex flex-col gap-3")[
                        Ui.ColorPickerInput.Placeholder("#000000"),
                        Ui.Separator,
                        Ui.ColorPickerArea,
                        Ui.ColorPickerSlider.Channel(Ui.ColorPickerChannel.Hue)
                    ]
                ]),
                Example("dropper", Ui.ColorPicker.Value("#dec0de").Dropper()),
                Example("clearable", Ui.ColorPicker.Value(_cleared).OnChange(color => _cleared = color).Clearable()),
                Example("copyable", Ui.ColorPicker.Value("#0ff1ce").Copyable()),
                Example("disabled", Ui.ColorPicker.Value("#beefed").Disabled()),
                P.Key("state").Class("text-sm text-ui-muted sm:col-span-3").Data(Testid("ui-color-picker-state"))[
                    $"Theme: {Named(_theme)}. Tint: {Named(_tint)}. Palette: {Named(_palette)}. Clearable: {Named(_cleared)}."
                ],
                // In a form the colour travels with the save, and text that is no colour holds the save back.
                Form.Key("form").Model(_brand).OnSubmit(_ => _saved = Named(_brand.Accent)).Class("flex flex-col items-start gap-3 sm:col-span-3")
                    .Data(Testid("ui-color-picker-form"))[
                    Ui.ColorPicker.Bind(() => _brand.Accent).Label("Accent").Description("Saved with the form.").Id("ui-color-picker-accent"),
                    Ui.Button.Primary.Type(Ui.ButtonType.Submit)["Save"],
                    P.Class("text-sm text-ui-muted").Data(Testid("ui-color-picker-saved"))[$"Saved: {_saved}."]
                ]
            ]
        ];

    // The name the parity walk finds each picker by (scripts/flux/parity-color-picker.mjs --live).
    private static Component Example(string name, Component picker) => Div.Key(name).Data("color-picker-example", name)[picker];

    private static string Named(string color) => color.Length == 0 ? "none" : color;

    private static AttrBag Testid(string value) => new("testid", value);

    private sealed class Brand
    {
        public string Accent { get; set; } = "#6366f1";
    }
}
