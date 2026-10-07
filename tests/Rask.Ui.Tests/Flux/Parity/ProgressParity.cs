using Rask.Core;

namespace Rask.UiTests.Flux.Parity;

/// <summary>Flux UI's <c>components/progress</c> page, example by example.</summary>
/// <remarks>
/// Every example on that page sits in a 256px column, which is what makes a bar 256px wide; the column is
/// the docs page's, so it is an inline style here.
/// </remarks>
public sealed class ProgressParity : FluxParity
{
    // A STAND-IN for flux:slider, not rebuilt yet: plain elements styled to what Flux's measure, so the
    // example that places a bar under it can be compared whole. It goes when the slider lands. With it, the
    // two utilities these examples hand to Class, which an app's own Tailwind build would emit.
    private const string StandIns =
        "<style>"
        + ".mb-6{margin-bottom:24px}.flex{display:flex}"
        + ".standin-value{font-size:14px;line-height:20px;font-variant-numeric:tabular-nums;color:oklch(0.552 0.016 285.938)}"
        + "ui-slider{display:flex;flex-direction:column;justify-content:center;min-height:16px;margin-bottom:16px}"
        + "ui-slider>div{display:flex;flex-direction:column;justify-content:center}"
        + ".standin-track{position:relative;flex-shrink:0;height:6px;border-radius:calc(infinity*1px);background:oklch(0.92 0.004 286.32)}"
        + ".standin-track>div:first-child{position:relative;height:100%;border-radius:inherit;overflow:hidden}"
        + ".standin-indicator{position:absolute;inset:0 50% 0 0;background:oklch(0.274 0.006 286.033)}"
        + ".standin-thumb{position:absolute;top:-5px;left:calc(50% - 8px);width:16px;height:16px;border-radius:calc(infinity*1px);"
        + "background:#fff;box-shadow:0 0 #0000,0 0 #0000,0 0 #0000,0 0 0 1px color-mix(in oklab,#000 15%,transparent),"
        + "0 1px 2px 0 rgb(0 0 0/.05),0 2px 4px 0 rgb(0 0 0/.1)}"
        + ".standin-thumb>input{position:absolute;width:1px;height:1px;margin:-1px;overflow:hidden;white-space:nowrap}"
        + ".dark .standin-value{color:oklch(0.705 0.015 286.067)}"
        + ".dark .standin-track{background:color-mix(in oklab,#fff 10%,transparent)}"
        + ".dark .standin-indicator{background:#fff}"
        + ".dark .standin-thumb{box-shadow:0 0 #0000,0 0 #0000,0 0 #0000,0 0 0 1px color-mix(in oklab,#000 30%,transparent),"
        + "0 1px 2px 0 rgb(0 0 0/.05),0 2px 4px 0 rgb(0 0 0/.1)}"
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
        // the bar's Value, and the slider is a stand-in resting where Flux's does, at 50.
        yield return ("controlled", Column(
            Raw.Value(
                "<ui-slider data-ui-control data-ui-slider tabindex=\"-1\">"
                + "<div data-ui-slider-track><div data-ui-slider-track class=\"standin-track\">"
                + "<div><div data-ui-slider-indicator class=\"standin-indicator\"></div></div>"
                + "<div data-ui-slider-thumb class=\"standin-thumb\"><input type=\"range\" min=\"0\" max=\"100\" step=\"1\"></div>"
                + "</div></div></ui-slider>"),
            Ui.Progress.Value(50)));
    }

    private static Component Column(params Component[] items) =>
        Div.Style("width:256px;margin:0 auto")[Raw.Value(StandIns), items];
}
