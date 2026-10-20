namespace Rask.Site.Features.UiKit;

/// <summary>Flux UI's text page, example for example: inks, sizes and the link in its three variants.</summary>
public sealed partial class UiKitTextDemo : Component
{
    /// <inheritdoc />
    protected override Component? Render() =>
        Div.Data("testid", "ui-text")[
            UiKitExample.Key("inks").Heading("Variants").Blurb("Three inks, or any Tailwind hue.")[
                Div.Class("flex flex-col gap-2")[
                    Ui.Text.Key("strong").Variant(Ui.TextVariant.Strong)["Strong text color"],
                    Ui.Text.Key("default")["Default text color"],
                    Ui.Text.Key("subtle").Subtle["Subtle text color"],
                    Ui.Text.Key("blue").Color(Ui.Color.Blue)["Colored text"]
                ]
            ],
            UiKitExample.Key("sizes").Heading("Size")[
                Div.Class("flex flex-col gap-2")[
                    Ui.Text.Key("lg").Lg["Larger text size"],
                    Ui.Text.Key("default")["Default text size"],
                    Ui.Text.Key("sm").Sm["Smaller text"]
                ]
            ],
            UiKitExample.Key("link").Heading("Link")
                .Blurb("A generated route stays in the app; a URL leaves it, in a new tab with External().")[
                Ui.Text.Key("in-text")["Visit our ",
                    Ui.Link.Key("docs").Href("https://rask.sh/docs").External()["documentation"], " for more information."]
            ],
            UiKitExample.Key("variants").Heading("Link variants")[
                Div.Class("flex flex-col gap-2")[
                    Ui.Text.Key("default")[Ui.Link.Key("l").Href("#")["Default link"]],
                    Ui.Text.Key("ghost")[Ui.Link.Key("l").Href("#").Ghost["Ghost link"]],
                    Ui.Text.Key("subtle")[Ui.Link.Key("l").Href("#").Subtle["Subtle link"]],
                    Ui.Text.Key("button")[Ui.Link.Key("l").As(Ui.LinkAs.Button)["Create new account →"]]
                ]
            ]
        ];
}
