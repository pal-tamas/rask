namespace Company.RaskServer.Features.Auth;

// The card every sign-in page sits in. Yours to restyle: it is the UI kit's components and plain Tailwind,
// compiled by this app's own Tailwind from Styles/app.css like every other page.
public abstract partial class AuthPage : Component
{
    // Named Content rather than Body: inside a markup host Body is the <body> element's chain entry.
    protected abstract Component? Content { get; }

    protected sealed override Component? Render() =>
        Div.Class("flex min-h-screen flex-col items-center justify-center bg-zinc-50 px-4 py-12 dark:bg-zinc-800")[
            Ui.Card.Class("w-full max-w-sm")[
                Div.Class("flex flex-col gap-6")[Content]
            ]
        ];

    protected static Component Title(string text) => Ui.Heading.Level(1).Xl[text];

    protected static Component Error(string id, Component? message) =>
        Ui.Callout.Danger.Icon(Ui.IconName.XCircle).Role("alert").Id(id)[Ui.CalloutHeading[message]];

    protected static Component Ok(string id, Component? message) =>
        Ui.Callout.Success.Icon(Ui.IconName.CheckCircle).Role("alert").Id(id)[Ui.CalloutHeading[message]];
}
