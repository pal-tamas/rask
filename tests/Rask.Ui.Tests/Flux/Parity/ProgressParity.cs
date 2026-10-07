using Rask.Core;

namespace Rask.UiTests.Flux.Parity;

/// <summary>Flux UI's <c>components/progress</c> page, example by example.</summary>
/// <remarks>
/// Every example on that page sits in a 256px column, which is what makes a bar 256px wide; the column is
/// the docs page's, so it is an inline style here.
/// </remarks>
public sealed class ProgressParity : FluxParity
{
    // The utilities these examples hand to Class, which an app's own Tailwind build would emit, and the value
    // beside a bar: a plain span on Flux's page.
    private const string StandIns =
        "<style>"
        + ".mb-4{margin-bottom:16px}.mb-6{margin-bottom:24px}.flex{display:flex}"
        + ".standin-value{font-size:14px;line-height:20px;font-variant-numeric:tabular-nums;color:oklch(0.552 0.016 285.938)}"
        + ".dark .standin-value{color:oklch(0.705 0.015 286.067)}"
        + "</style>";

    public override string Page => "progress";

    public override IEnumerable<(string Section, Component Example)> Examples()
    {
        yield return ("", Column(Ui.Progress.Value(75)));

        yield return ("max", Column(Ui.Progress.Value(3).Max(7)));

        yield return ("color", Column(Ui.Progress.Value(75).Color(Ui.Color.Purple)));

        // class="h-3" there; a utility written here is in no stylesheet, so the height is inline.
        yield return ("height", Column(Ui.Progress.Value(75).Style("height:12px")));

        yield return ("with-label", Column(
            Ui.Field[
                Ui.Label["Upload progress"],
                Ui.Progress.Value(42).Color(Ui.Color.Blue),
                Ui.Description["Uploading 3 of 7 files..."]
            ]));

        yield return ("display-value", Column(
            Ui.Field.Class("mb-6")[
                Ui.Label.Class("flex").Trailing(Span.Style("font-variant-numeric:tabular-nums")["75%"])["Storage"],
                Ui.Progress.Value(75)
            ],
            Ui.Field[
                Ui.Label["Storage"],
                Div.Style("display:flex;align-items:center;gap:16px;margin-top:-8px")[
                    Ui.Progress.Value(75),
                    Span.Class("standin-value")["75%"]
                ]
            ]));

        // wire:model there, on a slider and the bar both: Livewire's binding. Here the page's own state is
        // the slider's Value and the bar's.
        yield return ("controlled", Column(
            Ui.Slider.Value(50).Class("mb-4"),
            Ui.Progress.Value(50)));
    }

    private static Component Column(params Component[] items) =>
        Div.Style("width:256px;margin:0 auto")[Raw.Value(StandIns), items];
}
