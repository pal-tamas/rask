using Rask.Core;

namespace Rask.UiTests.Flux.Parity;

/// <summary>
///     The same eleven examples, each measured with its popover open — <c>scripts/flux/parity-color-picker.mjs</c>.
/// </summary>
/// <remarks>
///     <c>lib.mjs</c> presses whatever carries <c>data-parity-open</c> before it measures an example. The mark is
///     the measurer's and no prop of the picker, so the example puts it on what a reader presses: the swatch
///     button of an input trigger, or the button trigger itself. The button opens its dialog by command, with
///     no script at all.
/// </remarks>
public sealed partial class ColorPickerOpenParity : ColorPickerParity
{
    private const string Mark =
        "<script>document.currentScript.parentElement"
        + ".querySelector('[data-ui-color-picker-swatch-button], [data-ui-color-picker-button]')"
        + ".setAttribute('data-parity-open', '')</script>";

    // Flux's page goes on below its last example; this one ends there. Without the room, the last popovers
    // would have to open upwards.
    private const string Room = "<style>body{padding-bottom:900px}</style>";

    public override string Page => "color-picker-open";

    public override IEnumerable<(string Section, Component Example)> Examples() =>
        base.Examples().Select(example => (example.Section, (Component)Div[example.Example, Raw.Value(Room + Mark)]));
}
