using System.Globalization;
using Rask.Core;

namespace Rask.UiTests.Flux.Parity;

/// <summary>
///     <c>fluxui.dev/components/callout</c>, example for example.
/// </summary>
/// <remarks>
///     <para>
///     The buttons in the <c>actions</c> and <c>controls</c> slots are <c>Ui.Button</c>. The badge beside
///     one heading is Flux's badge, which is not rebuilt yet: a stand-in, a box of the size Flux's measures,
///     marked <c>data-parity-skip</c> so the tool holds it to its place and its size and leaves its inside to
///     that component's own page. Everything else — the callout, its icon, heading, text and link, the
///     buttons, and where the slots sit — is compared whole.
///     </para>
///     <para>
///     Two things on that page are the page's rather than the component's, and are written here as an app
///     would write them: the heading rows of "Dismissible" and "Engagement prompt" are a flex row AROUND the
///     heading, and "Premium upsell" hands its actions slot <c>@md:h-full m-0!</c>, which is Blade's way of
///     putting a class on a slot.
///     </para>
/// </remarks>
public sealed partial class CalloutParity : FluxParity
{
    // What an app's own Tailwind build emits for the classes these examples hand to Class, and the two
    // things Flux's page styles by hand.
    private const string AppUtilities =
        "<style>.mb-6{margin-bottom:24px}.mt-6{margin-top:24px}"
        + ".parity-strong{font-weight:500;color:oklch(27.4% .006 286.033)}.dark .parity-strong{color:#fff}"
        + ".parity-full-height [data-slot=actions]{height:100%;margin:0!important}</style>";

    // Flux's page is 654px wide where an example sits, less its 24px of padding either side.
    private const string Narrow = "width:100%;max-width:448px";

    private const string Wide = "width:100%;padding:0 32px";

    private const string HeadingRow = "display:flex;gap:8px;align-items:flex-start";

    private const string AlarmClock =
        "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"24\" height=\"24\" viewBox=\"0 0 24 24\" fill=\"none\""
        + " stroke=\"currentColor\" stroke-width=\"2\" stroke-linecap=\"round\" stroke-linejoin=\"round\">"
        + "<circle cx=\"12\" cy=\"13\" r=\"8\"/><path d=\"M12 9v4l2 2\"/><path d=\"M5 3 2 6\"/><path d=\"m22 6-3-3\"/>"
        + "<path d=\"M6.38 18.7 4 21\"/><path d=\"M17.64 18.67 20 21\"/></svg>";

    private static readonly Ui.Color[] Colors =
    [
        Ui.Color.Zinc, Ui.Color.Red, Ui.Color.Orange, Ui.Color.Amber, Ui.Color.Yellow, Ui.Color.Lime,
        Ui.Color.Green, Ui.Color.Emerald, Ui.Color.Teal, Ui.Color.Cyan, Ui.Color.Sky, Ui.Color.Blue,
        Ui.Color.Indigo, Ui.Color.Violet, Ui.Color.Purple, Ui.Color.Fuchsia, Ui.Color.Pink, Ui.Color.Rose,
    ];

    public override string Page => "callout";

    public override IEnumerable<(string Section, Component Example)> Examples()
    {
        yield return ("", Frame(
            Narrow,
            Raw.Value(AppUtilities),
            Ui.Callout.Icon(Ui.IconName.Clock)[
                Ui.CalloutHeading["Upcoming maintenance"],
                Ui.CalloutText[
                    "Our servers will be undergoing scheduled maintenance this Sunday ",
                    Span["from 2 AM - 5 AM UTC. Some services may be temporarily unavailable"],
                    ". ",
                    Ui.CalloutLink.Href("#")["Learn more"]
                ]
            ]));

        yield return ("icon-inside-heading", Frame(
            "width:100%;max-width:320px",
            Ui.Callout[
                Ui.CalloutHeading.Icon(Ui.IconName.Newspaper)["Policy update"],
                Ui.CalloutText["We've updated our Terms of Service and Privacy Policy. Please review them to stay informed."]
            ]));

        yield return ("with-actions", Frame(
            "width:100%;max-width:512px",
            Ui.Callout.Icon(Ui.IconName.Clock).Actions([Ui.Button["Renew now"], Ui.Button.Ghost.Href("/pricing")["View plans"]])[
                Ui.CalloutHeading["Subscription expiring soon"],
                Ui.CalloutText["Your current plan will expire in 3 days. Renew now to avoid service interruption and continue accessing premium features."]
            ]));

        yield return ("inline-actions", Frame(
            Wide,
            Ui.Callout.Icon(Ui.IconName.Cube).Secondary.Inline().Class("mb-6")
                .Actions([Ui.Button["Track order ->"], Ui.Button.Ghost["Reschedule"]])[
                Ui.CalloutHeading["Your package is delayed"]
            ],
            Ui.Callout.Icon(Ui.IconName.ExclamationTriangle).Secondary.Inline().Actions(Ui.Button["Update billing"])[
                Ui.CalloutHeading["Payment issue detected"],
                Ui.CalloutText["Your last payment attempt failed. Update your billing details to prevent service interruption."]
            ]));

        yield return ("dismissible", Frame(
            Wide,
            Div[
                Ui.Callout.Icon(Ui.IconName.Bell).Secondary.Inline().Controls(Ui.Button.Ghost.Icon(Ui.IconName.XMark))[
                    Div.Style(HeadingRow)[Ui.CalloutHeading["Upcoming meeting"], Ui.CalloutText["10:00 AM"]]
                ]
            ],
            // The two wrapping divs carry Alpine's collapse and fade on Flux's page; they take no room.
            Div[Div[
                Ui.Callout.Icon(Ui.IconName.FingerPrint).Secondary.Class("mt-6")
                    .Actions([Ui.Button["Change password"], Ui.Button.Ghost["Review activity"]])
                    .Controls(Ui.Button.Ghost.Icon(Ui.IconName.XMark))[
                    Ui.CalloutHeading["Unusual login attempt"],
                    Ui.CalloutText[
                        "We detected a login from a new device in ",
                        Span.Class("parity-strong")["New York, USA"],
                        ". If this was you, no action is needed. If not, secure your account immediately."
                    ]
                ]
            ]]));

        yield return ("variants", Frame(
            Narrow,
            Ui.Callout.Secondary.Icon(Ui.IconName.InformationCircle).Heading("Your account has been successfully created.").Class("mb-6"),
            Ui.Callout.Success.Icon(Ui.IconName.CheckCircle).Heading("Your account is verified and ready to use.").Class("mb-6"),
            Ui.Callout.Warning.Icon(Ui.IconName.ExclamationCircle).Heading("Please verify your account to unlock all features.").Class("mb-6"),
            Ui.Callout.Danger.Icon(Ui.IconName.XCircle).Heading("Something went wrong. Try again or contact support.")));

        yield return ("colors", Frame(
            "width:100%;max-width:512px",
            SpaceY(4, [.. Colors.Select(Message)])));

        yield return ("custom-icon", Frame(
            "width:100%;max-width:384px",
            Ui.Callout.CustomIcon(Raw.Value(AlarmClock))[
                Ui.CalloutHeading["Notification system updated"],
                Ui.CalloutText[P["We've improved our notification system to deliver alerts faster and more reliably."]]
            ]));

        // Feature spotlight.
        yield return ("examples", Frame(
            Wide,
            Ui.Callout.Icon(Ui.IconName.Sparkles).Color(Ui.Color.Purple)[
                Ui.CalloutHeading["Have a question?"],
                Ui.CalloutText[
                    "Try our new AI assistant, Jeffrey. Let him handle tasks and answer questions for you. ",
                    Ui.CalloutLink.Href("#")["Learn more"]
                ]
            ]));

        // Premium upsell.
        yield return ("examples", Frame(
            Wide,
            Ui.Callout.Icon(Ui.IconName.ShieldCheck).Color(Ui.Color.Blue).Inline().Class("parity-full-height")
                .Actions(Ui.Button["Upgrade to Pro ->"])[
                Ui.CalloutHeading["API access is restricted"],
                Ui.CalloutText["Get access to all of our premium features and benefits."]
            ]));

        // Upgrade offer.
        yield return ("examples", Frame(
            Wide,
            Ui.Callout.Icon(Ui.IconName.Banknotes).Color(Ui.Color.Lime).Inline().Actions(Ui.Button["Switch now ->"])[
                Ui.CalloutHeading["You could save $4,900/yr on annual billing."]
            ]));

        // Engagement prompt.
        yield return ("examples", Frame(
            Wide,
            Ui.Callout.Secondary.Icon(Ui.IconName.UserGroup)
                .Actions([Ui.Button["Invite member"], Ui.Button.Ghost["Manage team"]])[
                Div.Style(HeadingRow)[
                    Ui.CalloutHeading["Team collaboration"],
                    // Flux's badge, inset top and bottom: 24px tall in a 20px line.
                    StandIn("Available with Pro", 117.73, "height:24px;margin:-4px 0;border-radius:6px;font-size:12px")
                ],
                Ui.CalloutText[P["Share projects, manage permissions, and collaborate in real time with your team. Upgrade now to access these features."]]
            ]));
    }

    private static Component Message(Ui.Color color) =>
        Ui.Callout.Color(color).Icon(Ui.IconName.ExclamationCircle).Inline().Heading("You've received a new message.")
            .Actions(Ui.Button["View message"]);

    // The 606px an example has on Flux's page, with what holds the callout centred in it.
    private static Component Frame(string holder, params Component[] items) =>
        Div.Style("width:606px;display:flex;justify-content:center")[Div.Style(holder)[items]];

    // A component from another page that is not rebuilt yet: the room Flux's takes, and nothing else.
    private static Component StandIn(string label, double width, string shape = "height:40px;border-radius:8px;font-size:14px") =>
        Div.Data("parity-skip", "")
            .Style(string.Create(
                CultureInfo.InvariantCulture,
                $"width:{width}px;flex:none;display:flex;align-items:center;justify-content:center;border:1px solid #d4d4d8;font-weight:500;white-space:nowrap;{shape}"))[
            label
        ];
}
