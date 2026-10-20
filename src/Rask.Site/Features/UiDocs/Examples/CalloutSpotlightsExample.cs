namespace Rask.Site.Features.UiDocs.Examples;

public sealed partial class CalloutSpotlightsExample : Component
{
    protected override Component? Render() =>
        Div.Class("space-y-3")[
            Ui.Callout.Icon(Ui.IconName.Sparkles).Color(Ui.Color.Purple)[
                Ui.CalloutHeading["Have a question?"],
                Ui.CalloutText[
                    "Try our new AI assistant, Jeffrey. Let him handle tasks and answer questions for you. ",
                    Ui.CalloutLink.Href("#")["Learn more"]
                ]
            ],
            Ui.Callout.Icon(Ui.IconName.ShieldCheck).Color(Ui.Color.Blue).Inline().Actions(Ui.Button["Upgrade to Pro ->"])[
                Ui.CalloutHeading["API access is restricted"],
                Ui.CalloutText["Get access to all of our premium features and benefits."]
            ],
            Ui.Callout.Icon(Ui.IconName.Banknotes).Color(Ui.Color.Lime).Inline().Actions(Ui.Button["Switch now ->"])[
                Ui.CalloutHeading["You could save $4,900/yr on annual billing."]
            ],
            Ui.Callout.Secondary.Icon(Ui.IconName.UserGroup).Actions([Ui.Button["Invite member"], Ui.Button.Ghost["Manage team"]])[
                Div.Class("flex items-start gap-2 @max-md:flex-col")[
                    Ui.CalloutHeading["Team collaboration"],
                    Ui.Badge.Sm["Available with Pro"]
                ],
                Ui.CalloutText["Share projects, manage permissions, and collaborate in real time with your team. Upgrade now to access these features."]
            ]
        ];
}
