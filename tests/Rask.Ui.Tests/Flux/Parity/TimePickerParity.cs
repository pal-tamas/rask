using Rask.Core;

namespace Rask.UiTests.Flux.Parity;

/// <summary><c>fluxui.dev/components/time-picker</c>, example by example, as the page loads: every list closed.</summary>
public sealed partial class TimePickerParity : TimePickerExamples
{
    public override string Page => "time-picker";

    protected override Component? Mark => null;
}

/// <summary>
///     The same examples with each list open, for <c>scripts/flux/parity-date.mjs time-picker</c>.
/// </summary>
/// <remarks>
///     The measurer opens whatever carries <c>data-parity-open</c> with a real press at its centre, and
///     <c>parity-date.mjs</c> marks Flux's page the way the script below marks this one: the button, or the picker
///     itself where the trigger is typed. This page is static — the kit's sheet and nothing else — so a typed
///     trigger, whose list the RUNTIME shows when C# says so (<c>data-rask-popover-open</c>), is shown here by
///     the same script.
/// </remarks>
public sealed partial class TimePickerOpenParity : TimePickerExamples
{
    private const string Script =
        // Room under the last example, as Flux's page has: a list at the foot of the page opens downwards on both.
        "<script>document.body.style.paddingBottom = '900px';"
        + "for (const picker of document.currentScript.parentElement.querySelectorAll('[data-ui-time-picker]')) {"
        + "const button = picker.querySelector('[data-ui-time-picker-button]');"
        + "(button ?? picker).setAttribute('data-parity-open', '');"
        + "if (!button) picker.addEventListener('click', () => picker.querySelector('[popover][role=listbox]')?.togglePopover());"
        + "}</script>";

    public override string Page => "time-picker-open";

    protected override Component? Mark => Raw.Value(Script);
}

/// <summary>The ten examples on Flux's time picker page, in its order and under its section ids.</summary>
public abstract partial class TimePickerExamples : FluxParity
{
    // Flux's examples sit in a centred row, the picker in an item as wide as its content: its own 240px.
    private const string Centre = "display:flex;justify-content:center";

    // What the docs page is to these examples. Its theme's monospace face is MonoLisa, a licensed webfont the
    // typed fields are set in and sized by (two of its `ch`): here a local monospace scaled to the same advance,
    // 8.96px at 14px where a system face is 8.40px. And the width its "multiple" example hands the picker by class.
    private const string App =
        "<style>@font-face{font-family:MonoLisa;src:local('Menlo'),local('Courier New'),local('DejaVu Sans Mono');size-adjust:106.649%}"
        + ":root{--font-mono:MonoLisa,monospace}.parity-w-60{max-width:240px}</style>";

    /// <summary>What marks an example's pickers for the measurer to open, on the page that is measured open.</summary>
    protected abstract Component? Mark { get; }

    public override IEnumerable<(string Section, Component Example)> Examples()
    {
        yield return ("", One(Ui.TimePicker.Of<TimeOnly?>()));

        // "Basic usage" has no rendered example on Flux's page.
        yield return ("input-trigger", One(Ui.TimePicker.Of<TimeOnly?>().Type(Ui.TimePickerType.Input)));

        yield return ("without-dropdown", One(Ui.TimePicker.Of<TimeOnly?>().Type(Ui.TimePickerType.Input).Dropdown(false)));

        yield return ("multiple-times", One(Ui.TimePicker.Of<List<TimeOnly>>().Class("parity-w-60"), row: true));

        yield return ("time-format", Div.Style(Centre)[
            Div.Style("display:flex;gap:16px;padding:0 8px")[
                Ui.TimePicker.Of<TimeOnly?>().TwelveHour.Label("12-hour").Id("twelve"),
                Ui.TimePicker.Of<TimeOnly?>().TwentyFourHour.Label("24-hour").Id("twenty-four"),
                Mark
            ]
        ]);

        yield return ("interval", One(Ui.TimePicker.Of<TimeOnly?>().Interval(60)));

        yield return ("min/max-times", One(Ui.TimePicker.Of<TimeOnly?>().Min(new TimeOnly(9, 0)).Max(new TimeOnly(17, 0)), row: true));

        yield return ("unavailable-times", One(Ui.TimePicker.Of<TimeOnly?>().Unavailable([
            new TimeOnly(3, 0),
            new TimeOnly(4, 0),
            new UiTimeRange(new TimeOnly(5, 30), new TimeOnly(7, 29)),
        ])));

        yield return ("open-to", One(Ui.TimePicker.Of<TimeOnly?>().OpenTo(new TimeOnly(10, 0))));

        yield return ("localization", One(Ui.TimePicker.Of<TimeOnly?>().Locale("ja-JP")));
    }

    // The item the picker sits in is a block on Flux's page, and a row of its own in two of the examples.
    private Component One(Component picker, bool row = false) =>
        Div.Style(Centre)[Raw.Value(App), Div.Style(row ? "display:flex" : null)[picker, Mark]];
}
