namespace Rask.Site.Features.UiKit;

/// <summary>
///     Every example on Flux UI's callout page, drawn with <c>Ui.Callout</c>.
/// </summary>
/// <remarks>
///     Dismissing is the page's, as it is in Flux: the callout places the control and the demo decides what
///     pressing it does — here, two fields.
/// </remarks>
public sealed partial class UiKitCalloutDemo : Component
{
    private const string AlarmClock =
        "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"24\" height=\"24\" viewBox=\"0 0 24 24\" fill=\"none\" "
        + "stroke=\"currentColor\" stroke-width=\"2\" stroke-linecap=\"round\" stroke-linejoin=\"round\" aria-hidden=\"true\">"
        + "<circle cx=\"12\" cy=\"13\" r=\"8\"/><path d=\"M12 9v4l2 2\"/><path d=\"M5 3 2 6\"/><path d=\"m22 6-3-3\"/>"
        + "<path d=\"M6.38 18.7 4 21\"/><path d=\"M17.64 18.67 20 21\"/></svg>";

    private static readonly Ui.Color[] Colors =
    [
        Ui.Color.Zinc, Ui.Color.Red, Ui.Color.Orange, Ui.Color.Amber, Ui.Color.Yellow, Ui.Color.Lime,
        Ui.Color.Green, Ui.Color.Emerald, Ui.Color.Teal, Ui.Color.Cyan, Ui.Color.Sky, Ui.Color.Blue,
        Ui.Color.Indigo, Ui.Color.Violet, Ui.Color.Purple, Ui.Color.Fuchsia, Ui.Color.Pink, Ui.Color.Rose,
    ];

    private bool _meeting = true;
    private bool _login = true;

    /// <inheritdoc />
    protected override Component? Render() =>
        Div.Data("testid", "ui-callout").Class("max-w-2xl space-y-8")[
            Basics(),
            Actions(),
            Dismissible(),
            Variants(),
            Example("Colors", [.. Colors.Select(Message)]),
            Spotlights()
        ];

    private static Component Basics() =>
    [
        Example(
            "Callout",
            Ui.Callout.Icon(Ui.IconName.Clock)[
                Ui.CalloutHeading["Upcoming maintenance"],
                Ui.CalloutText[
                    "Our servers will be undergoing scheduled maintenance this Sunday from 2 AM - 5 AM UTC. "
                    + "Some services may be temporarily unavailable. ",
                    Ui.CalloutLink.Href(Routes.UiKitFeedbackPage())["Learn more"]
                ]
            ]),
        Example(
            "Icon inside heading",
            Ui.Callout[
                Ui.CalloutHeading.Icon(Ui.IconName.Newspaper)["Policy update"],
                Ui.CalloutText["We've updated our Terms of Service and Privacy Policy. Please review them to stay informed."]
            ]),
        Example(
            "Custom icon",
            Ui.Callout.CustomIcon(Raw.Value(AlarmClock))[
                Ui.CalloutHeading["Notification system updated"],
                Ui.CalloutText[P["We've improved our notification system to deliver alerts faster and more reliably."]]
            ])
    ];

    private static Component Actions() =>
    [
        Example(
            "With actions",
            Ui.Callout.Icon(Ui.IconName.Clock).Actions([Ui.Button.Key("renew")["Renew now"], Ui.Button.Key("plans").Ghost["View plans"]])[
                Ui.CalloutHeading["Subscription expiring soon"],
                Ui.CalloutText["Your current plan will expire in 3 days. Renew now to avoid service interruption and continue accessing premium features."]
            ]),
        Example(
            "Inline actions",
            Ui.Callout.Icon(Ui.IconName.Cube).Secondary.Inline()
                .Actions([Ui.Button.Key("track")["Track order ->"], Ui.Button.Key("later").Ghost["Reschedule"]])[
                Ui.CalloutHeading["Your package is delayed"]
            ],
            Ui.Callout.Icon(Ui.IconName.ExclamationTriangle).Secondary.Inline().Actions(Ui.Button["Update billing"])[
                Ui.CalloutHeading["Payment issue detected"],
                Ui.CalloutText["Your last payment attempt failed. Update your billing details to prevent service interruption."]
            ])
    ];

    private Component Dismissible() =>
        Example(
            "Dismissible",
            _meeting
                ? Ui.Callout.Key("meeting").Icon(Ui.IconName.Bell).Secondary.Inline().Controls(Dismiss(() => _meeting = false))[
                    Div.Class("flex gap-2 @max-md:flex-col items-start")[
                        Ui.CalloutHeading["Upcoming meeting"],
                        Ui.CalloutText["10:00 AM"]
                    ]
                ]
                : null,
            _login
                ? Ui.Callout.Key("login").Icon(Ui.IconName.FingerPrint).Secondary
                    .Actions([Ui.Button.Key("password")["Change password"], Ui.Button.Key("review").Ghost["Review activity"]])
                    .Controls(Dismiss(() => _login = false))[
                    Ui.CalloutHeading["Unusual login attempt"],
                    Ui.CalloutText[
                        "We detected a login from a new device in ",
                        Span.Class("font-medium text-zinc-800 dark:text-white")["New York, USA"],
                        ". If this was you, no action is needed. If not, secure your account immediately."
                    ]
                ]
                : null,
            _meeting && _login
                ? null
                : Ui.Button.Key("restore").Sm.OnClick(() => { _meeting = true; _login = true; })["Show them again"]);

    private static Component Variants() =>
        Example(
            "Variants",
            Ui.Callout.Secondary.Icon(Ui.IconName.InformationCircle).Heading("Your account has been successfully created."),
            Ui.Callout.Success.Icon(Ui.IconName.CheckCircle).Heading("Your account is verified and ready to use."),
            Ui.Callout.Warning.Icon(Ui.IconName.ExclamationCircle).Heading("Please verify your account to unlock all features."),
            Ui.Callout.Danger.Icon(Ui.IconName.XCircle).Heading("Something went wrong. Try again or contact support."));

    private static Component Spotlights() =>
        Example(
            "Examples",
            Ui.Callout.Icon(Ui.IconName.Sparkles).Color(Ui.Color.Purple)[
                Ui.CalloutHeading["Have a question?"],
                Ui.CalloutText[
                    "Try our new AI assistant, Jeffrey. Let him handle tasks and answer questions for you. ",
                    Ui.CalloutLink.Href(Routes.UiKitFeedbackPage())["Learn more"]
                ]
            ],
            Ui.Callout.Icon(Ui.IconName.ShieldCheck).Color(Ui.Color.Blue).Inline().Actions(Ui.Button["Upgrade to Pro ->"])[
                Ui.CalloutHeading["API access is restricted"],
                Ui.CalloutText["Get access to all of our premium features and benefits."]
            ],
            Ui.Callout.Icon(Ui.IconName.Banknotes).Color(Ui.Color.Lime).Inline().Actions(Ui.Button["Switch now ->"])[
                Ui.CalloutHeading["You could save $4,900/yr on annual billing."]
            ],
            Ui.Callout.Secondary.Icon(Ui.IconName.UserGroup)
                .Actions([Ui.Button.Key("invite")["Invite member"], Ui.Button.Key("manage").Ghost.Class("@max-md:hidden")["Manage team"]])[
                Div.Class("flex gap-2 @max-md:flex-col items-start")[
                    Ui.CalloutHeading["Team collaboration"],
                    Ui.Badge.Sm["Available with Pro"]
                ],
                Ui.CalloutText[P["Share projects, manage permissions, and collaborate in real time with your team. Upgrade now to access these features."]]
            ]);

    private static Component Message(Ui.Color color) =>
        Ui.Callout.Key(color.ToString()).Color(color).Icon(Ui.IconName.ExclamationCircle).Inline()
            .Heading("You've received a new message.")
            .Actions(Ui.Button["View message"]);

    private static Component Dismiss(Action dismiss) =>
        Ui.Button.Ghost.Square().AccessibleLabel("Dismiss").OnClick(dismiss)[Ui.Icon.Name(Ui.IconName.XMark).Mini];

    private static Component Example(string title, params Component?[] callouts) =>
        Div.Key(title).Data("example", title).Class("space-y-3")[
            H3.Class("text-sm font-medium text-ui-muted")[title],
            callouts
        ];
}
