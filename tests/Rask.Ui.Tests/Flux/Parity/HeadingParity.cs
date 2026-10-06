using Rask.Core;

namespace Rask.UiTests.Flux.Parity;

/// <summary>fluxui.dev/components/heading, example by example.</summary>
public sealed partial class HeadingParity : FluxParity
{
    // Flux's `class="mt-2"` and `class="mb-1"`: a utility written here is in no stylesheet.
    private const string Below = "margin-top:8px";

    public override string Page => "heading";

    public override IEnumerable<(string Section, Component Example)> Examples()
    {
        yield return ("", Div.Style("width:320px")[
            Ui.Heading["User profile"],
            Ui.Text.Style(Below)["This information will be displayed publicly."]
        ]);

        // The live page captions each size, and the longest caption is what sets the column's width.
        yield return ("sizes", Div.Style("width:fit-content;display:flex;flex-direction:column;gap:24px")[
            Div[
                Ui.Heading["Default"],
                Ui.Text.Style(Below)["14px · Use liberally—think input and toast labels."]
            ],
            Div[
                Ui.Heading.Lg["Large"],
                Ui.Text.Style(Below)["16px · Use sparingly—think modal and card headings."]
            ],
            Div[
                Ui.Heading.Xl["Extra large"],
                Ui.Text.Style(Below)["24px · Use rarely—think hero text."]
            ],
            Div[
                Ui.Heading.Xxl["Extra extra large"],
                Ui.Text.Style(Below)["36px · Use for prominent page titles and hero text."]
            ]
        ]);

        yield return ("heading-level", Div.Style("width:320px")[
            Ui.Heading.Level(3)["User profile"],
            Ui.Text.Style(Below)["This information will be displayed publicly."]
        ]);

        yield return ("examples", Div.Style("width:240px")[
            Ui.Text["Year to date"],
            Ui.Heading.Xl.Style("margin-bottom:4px")["$7,532.16"],
            // The docs page's own prose size, which the icon inherits there.
            Div.Class("parity-up").Style("display:flex;align-items:center;gap:8px;font-size:16px;line-height:26px")[
                TrendingUp(),
                Span.Style("font-size:14px;line-height:20px")["15.2%"]
            ]
        ]);
    }

    // A stand-in for `flux:icon.arrow-trending-up variant="micro"`, which Flux marks and so the pairing counts.
    // Ui.Icon is not Flux's yet; when it is, this becomes Ui.Icon and the <style> goes. Green-600, green-500 in dark.
    private static Component TrendingUp() => Raw.Value(
        "<style>.parity-up{color:oklch(0.627 0.194 149.214)}.dark .parity-up{color:oklch(0.723 0.219 149.579)}</style>"
        + "<svg data-ui-icon xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 16 16\" fill=\"currentColor\" aria-hidden=\"true\""
        + " style=\"width:16px;height:16px;flex-shrink:0\">"
        + "<path fill-rule=\"evenodd\" clip-rule=\"evenodd\" d=\"M9.808 4.057a.75.75 0 0 1 .92-.527l3.116.849a.75.75 0 0 1"
        + " .528.915l-.823 3.121a.75.75 0 0 1-1.45-.382l.337-1.281a23.484 23.484 0 0 0-3.609 3.056.75.75 0 0 1-1.07.01L6"
        + " 8.06l-3.72 3.72a.75.75 0 1 1-1.06-1.061l4.25-4.25a.75.75 0 0 1 1.06 0l1.756 1.755a25.015 25.015 0 0 1"
        + " 3.508-2.85l-1.46-.398a.75.75 0 0 1-.526-.92Z\"/></svg>");
}
