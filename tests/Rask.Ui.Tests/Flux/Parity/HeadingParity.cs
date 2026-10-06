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

    // `flux:icon.arrow-trending-up variant="micro"`, in the green an app's own Tailwind build would emit for
    // the wrapper's `text-green-600 dark:text-green-500`.
    private static Component[] TrendingUp() =>
    [
        Raw.Value("<style>.parity-up{color:oklch(0.627 0.194 149.214)}.dark .parity-up{color:oklch(0.723 0.219 149.579)}</style>"),
        Ui.Icon.Name(Ui.IconName.ArrowTrendingUp).Micro,
    ];
}
