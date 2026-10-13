using Rask.Core;

namespace Rask.UiTests.Flux.Parity;

/// <summary><c>fluxui.dev/components/color-picker</c>, example by example.</summary>
/// <remarks>
///     <para>
///     The colours and the palette are the rendered page's: its "format" example is one picker labelled
///     <c>rgba</c> where the markdown lists six, and its custom swatches are eight labelled pairs.
///     </para>
///     <para>
///     <c>scripts/flux/parity.mjs color-picker</c> compares every example as loaded, popover shut;
///     <c>scripts/flux/parity-color-picker.mjs</c> measures each with its popover open
///     (<see cref="ColorPickerOpenParity" />) and walks both pages.
///     </para>
/// </remarks>
public partial class ColorPickerParity : FluxParity
{
    // The column Flux's docs page sets every example in, and the picker's own width inside it.
    private const string Column = "display:flex;justify-content:center;max-width:384px;margin:0 auto";

    private const string Narrow = "width:240px";

    // What the runtime's `data-rask-requires` does, which this static page has no runtime to do: the dropper is
    // rendered hidden and shown where the browser has an EyeDropper.
    private const string Requires =
        "<script>document.currentScript.parentElement.querySelectorAll('[data-rask-requires]')"
        + ".forEach(el => { if (el.getAttribute('data-rask-requires') in window) el.removeAttribute('hidden'); })</script>";

    public override string Page => "color-picker";

    public override IEnumerable<(string Section, Component Example)> Examples()
    {
        yield return ("", Example(Ui.ColorPicker.Value("#bada55")));

        yield return ("format", Div.Style(Column)[
            Div.Style(Narrow + ";display:flex")[
                Ui.ColorPicker.Value("rgb(99 102 241 / 0.5)").Format(Ui.ColorPickerFormat.Rgba).Label("rgba")
            ]
        ]);

        yield return ("sizes", Div.Style(Column)[
            Div.Style(Narrow + ";display:flex;flex-direction:column;gap:16px")[
                Ui.ColorPicker.Value("#c0ffee"),
                Ui.ColorPicker.Value("#c0ffee").Sm,
                Ui.ColorPicker.Value("#c0ffee").Xs
            ]
        ]);

        yield return ("button-trigger", Example(Ui.ColorPicker.Value("#ca1ebb").Type(Ui.ColorPickerType.Button)));

        yield return ("custom-swatches", Example(Ui.ColorPicker.Value("#ef4444").Swatches(
        [
            ("#ef4444", "Red"), ("#f97316", "Orange"), ("#f59e0b", "Amber"), ("#22c55e", "Green"),
            ("#14b8a6", "Teal"), ("#3b82f6", "Blue"), ("#a855f7", "Purple"), ("#ec4899", "Pink"),
        ])));

        yield return ("no-swatches", Example(Ui.ColorPicker.Value("#ef4444").Swatches([])));

        yield return ("custom-layout", Example(Ui.ColorPicker.Value("#facade")[
            Div.Style("display:flex;flex-direction:column;gap:12px")[
                Ui.ColorPickerInput.Placeholder("#000000"),
                Ui.Separator,
                Ui.ColorPickerArea,
                Ui.ColorPickerSlider.Channel(Ui.ColorPickerChannel.Hue)
            ]
        ]));

        yield return ("dropper", Div[Example(Ui.ColorPicker.Value("#dec0de").Dropper()), Raw.Value(Requires)]);

        yield return ("clearable", Example(Ui.ColorPicker.Value("#deface").Clearable()));

        yield return ("copyable", Example(Ui.ColorPicker.Value("#0ff1ce").Copyable()));

        yield return ("disabled", Div.Style(Column)[
            Div.Style(Narrow + ";display:flex")[Ui.ColorPicker.Value("#beefed").Disabled()]
        ]);
    }

    private static Component Example(Component picker) => Div.Style(Column)[Div.Style(Narrow)[picker]];
}
