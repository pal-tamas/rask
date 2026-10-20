namespace Rask.Site.Features.UiKit;

/// <summary>Flux UI's heading page, example for example: sizes, an outline level apart from the size, and a heading led by text.</summary>
public sealed partial class UiKitHeadingDemo : Component
{
    /// <inheritdoc />
    protected override Component? Render() =>
        Div.Data("testid", "ui-heading")[
            UiKitExample.Key("sizes").Heading("Sizes").Blurb("14, 16, 24 and 36 pixels.")[
                Div.Class("flex flex-col gap-4")[
                    Ui.Heading.Key("base")["Default"],
                    Ui.Heading.Key("lg").Lg["Large"],
                    Ui.Heading.Key("xl").Xl["Extra large"],
                    Ui.Heading.Key("xxl").Xxl["Extra extra large"]
                ]
            ],
            UiKitExample.Key("level").Heading("Heading level")
                .Blurb("The outline level is its own step: this one is an <h3> at the base size.")[
                Ui.Heading.Key("h").Level(3)["User profile"],
                Ui.Text.Key("t").Class("mt-2")["This information will be displayed publicly."]
            ],
            UiKitExample.Key("leading").Heading("Leading subheading").Blurb("A line of text above a heading.")[
                Ui.Text.Key("t")["Year to date"],
                Ui.Heading.Key("h").Xl.Class("mb-1")["$7,532.16"],
                Ui.Text.Key("up").Color(Ui.Color.Green)["15.2%"]
            ]
        ];
}
