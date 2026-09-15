namespace Company.RaskServer.Features.Auth;

// The card every sign-in page sits in. Yours to restyle: the classes are daisyUI, compiled by this app's
// own Tailwind from Styles/app.css like every other page.
public abstract partial class AuthPage : Component
{
    // Named Content rather than Body: inside a markup host Body is the <body> element's chain entry.
    protected abstract Component? Content { get; }

    protected sealed override Component? Render() =>
        Div.Class("hero min-h-screen bg-base-200")[
            Div.Class("hero-content w-full max-w-sm flex-col")[
                Div.Class("card bg-base-100 w-full shadow-sm")[
                    Div.Class("card-body gap-4")[Content]
                ]
            ]
        ];

    // A real <label for>, so the caption is the field's accessible name and its click target.
    protected static Component Field(string id, string label, Component input) =>
        Div.Class("fieldset")[Label.For(id).Class("fieldset-legend")[label], input];

    protected static Component Error(string id, Component? message) =>
        Div.Class("alert alert-error").Role("alert").Id(id)[Span[message]];

    protected static Component Ok(string id, Component? message) =>
        Div.Class("alert alert-success").Role("alert").Id(id)[Span[message]];
}
