using Rask.Core;

namespace Rask.UiTests.Flux.Parity;

/// <summary>
///     The same fifteen pickers, each measured with its popup open — <c>scripts/flux/parity-date.mjs date-picker</c>.
/// </summary>
/// <remarks>
///     <para>
///     <c>lib.mjs</c> presses whatever carries <c>data-parity-open</c> before it measures an example. The mark is
///     the measurer's and no prop of the picker, so the example puts it on: on the picker itself where its
///     button fills it, and on the chevron of a typed field, where a press on a segment only puts the caret in it.
///     </para>
///     <para>
///     A button opens its popover with no script at all. A typed field opens it from C# — a handler this static
///     page has no runtime to run — so the example does what that handler does: <c>showPopover()</c>.
///     </para>
/// </remarks>
public sealed partial class DatePickerOpenParity : DatePickerParity
{
    private const string Mark =
        "<script>(() => {"
        + "const picker = document.currentScript.parentElement.querySelector('[data-ui-date-picker]');"
        + "const typed = picker.querySelector('[role=group]');"
        + "if (!typed) { picker.setAttribute('data-parity-open', ''); return; }"
        + "typed.addEventListener('click', () => picker.querySelector('dialog').showPopover());"
        + "typed.querySelector(':scope > svg:last-child').setAttribute('data-parity-open', '');"
        + "})()</script>";

    // Flux's page goes on below its last example; this one ends there. Without the room, the last popups would
    // have to open upwards.
    private const string Room = "<style>body{padding-bottom:900px}</style>";

    public override string Page => "date-picker-open";

    public override IEnumerable<(string Section, Component Example)> Examples() =>
        Pickers().Select(example => (example.Section, (Component)Div.Style("display:flex;justify-content:center;max-width:384px;margin:0 auto")[
            Div[example.Picker],
            Raw.Value(AppFont + Room + Mark)
        ]));
}
