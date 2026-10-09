using Rask.Core;

namespace Rask.UiTests.Flux.Parity;

/// <summary><c>fluxui.dev/components/slider</c>, example by example.</summary>
/// <remarks>
///     <para>
///     The values are the rendered page's, which says more than its markdown does: the first slider there runs
///     0–1000 and rests at 500, the range examples at 200–800 and 450–550.
///     </para>
///     <para>
///     Every example but the step dots sits in a 256px column; the column is the docs page's, so it is an inline
///     style here. <c>big-step</c> is on Flux's examples and not on these: see <c>FluxConformanceTests.NotTranslated</c>.
///     </para>
/// </remarks>
public sealed partial class SliderParity : FluxParity
{
    private const string Narrow = "width:256px;margin:0 auto";

    private const string Wide = "width:384px;margin:0 auto";

    // What an app's own Tailwind build emits for the classes these examples hand to the slider and the input
    // beside it: the kit's sheet holds only what the kit writes.
    private const string AppUtilities =
        "<style>.h-5{height:1.25rem}.size-5{width:1.25rem;height:1.25rem}.size-6{width:1.5rem;height:1.5rem}"
        + ".max-w-18{max-width:4.5rem}</style>";

    public override string Page => "slider";

    public override IEnumerable<(string Section, Component Example)> Examples()
    {
        yield return ("", Column(Narrow, Ui.Slider.Value(500).Max(1000)));

        yield return ("min/max/step", Column(Narrow, Ui.Slider.Value(50).Min(0).Max(100).Step(10)));

        // wire:text there: Livewire's. Here the number is the page's own state, drawn in the label.
        yield return ("displaying-value", Column(Narrow,
            Ui.Field[
                Ui.Label.Trailing(Span.Style("font-variant-numeric:tabular-nums")["50"])["Corner radius"],
                Ui.Slider.Value(50)
            ]));

        yield return ("with-input", Column(Narrow,
            Ui.Field[
                Ui.Label["Corner radius"],
                Div.Style("display:flex;align-items:center;gap:16px;margin-top:-8px")[
                    Ui.Slider.Value(25),
                    Ui.Input.Value(25).Type(InputType.Number).Sm.Class("max-w-18")
                ]
            ]));

        yield return ("big-steps", Column(Narrow,
            Ui.Field[
                Ui.Slider.Value(500).Min(0).Max(1000).Step(1).BigStep(100),
                Ui.Description["Hold ", Key("⇧"), " and press arrow keys to adjust the value by 10."]
            ]));

        yield return ("step-marks", Column(Narrow,
            Ui.Slider.Value(3).Min(1).Max(5)[Steps(step => Ui.SliderTick.Value(step))]));

        yield return ("step-dots", Column(Wide,
            Ui.Slider.Value(4).Min(1).Max(5).Inside.TrackClass("h-5").ThumbClass("size-6")[
                Steps(step => Ui.SliderTick.Value(step).Dot)
            ]));

        yield return ("numbered-steps", Column(Narrow,
            Ui.Slider.Value(3).Min(1).Max(5)[
                Steps(step => Ui.SliderTick.Value(step)[step.ToString(System.Globalization.CultureInfo.InvariantCulture)])
            ]));

        yield return ("custom-steps", Column(Narrow,
            Ui.Slider.Value(3).Min(1).Max(5)[
                Ui.SliderTick.Value(1)["Low"],
                Ui.SliderTick.Value(3)["Mid"],
                Ui.SliderTick.Value(5)["High"]
            ]));

        yield return ("range-slider", Column(Narrow, Ui.Slider.Value(new[] { 200, 800 }).Range().Max(1000)));

        // "Basic usage" has no rendered example; this one sits under "Min steps between".
        yield return ("basic-usage", Column(Narrow,
            Ui.Slider.Value(new[] { 450, 550 }).Range().Max(1000).Step(1).MinStepsBetween(100)));

        yield return ("basic-usage", Column(Narrow,
            Ui.Field[
                Ui.Label.Trailing(Price(200, 800))["Price range"],
                Ui.Slider.Value(new[] { 200, 800 }).Range().Min(0).Max(990).Step(10).MinStepsBetween(10).BigStep(100)
            ]));

        yield return ("custom-styles", Column(Narrow, Ui.Slider.Value(500).Max(1000).TrackClass("h-5").ThumbClass("size-5")));
    }

    private static Component Column(string width, params Component[] items) =>
        Div.Style(width)[Raw.Value(AppUtilities), items];

    // The docs page's own key cap, set in its own monospace face: a stand-in the size it measures there.
    private static Component Key(string cap) =>
        Span.Data("parity-skip").Style("display:inline-block;width:20.96875px;height:20px;overflow:hidden;white-space:nowrap;vertical-align:top")[cap];

    private static IEnumerable<Component> Steps(Func<int, Component> tick) =>
        Enumerable.Range(1, 5).Select(tick);

    // The docs page's own markup, white space and all: the text between the two numbers is what is compared.
    private static Component Price(int low, int high) =>
        Raw.Value(
            $"$<span style=\"font-variant-numeric:tabular-nums\">{low.ToString(System.Globalization.CultureInfo.InvariantCulture)}</span>"
            + "\n                            –\n                            $"
            + $"<span style=\"font-variant-numeric:tabular-nums\">{high.ToString(System.Globalization.CultureInfo.InvariantCulture)}</span>");
}
