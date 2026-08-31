namespace Rask.Example.Auth.Jwt;

// App shell. JwtBootstrap (headless) re-establishes the principal from ProtectedSessionStorage on a
// fresh session/refresh; the navbar + Router render the matched page. Bootstrap rides a CDN link and
// The palette lives in Styles/app.css and is compiled with it.
public sealed partial class App : Component
{
    protected override Component? HeadAssets =>
    [
        Title["Rask — JWT auth sample"],
        Meta.Charset("utf-8"),
        Meta.Name("viewport").Content("width=device-width, initial-scale=1"),
        Link.Rel("stylesheet").Href("/css/app.css")
    ];

    protected override string? BodyClass => "bg-slate-50 dark:bg-slate-900";

    protected override Component? Render() =>
    [
        JwtBootstrap,
        Nav.Class("flex items-center border-b border-slate-800 bg-slate-900 text-slate-100 shadow-sm")[
            Div.Class("mx-auto w-full max-w-6xl px-4")[
                NavLink.Href(Features.Routes.HomePage()).Class("app-brand font-bold")["Rask · JWT auth"],
                A
                    .Href("https://github.com/pal-tamas/rask")
                    .Target("_blank")
                    .Class("inline-flex items-center gap-1.5 rounded-md px-2.5 py-1.5 text-sm font-medium no-underline transition disabled:cursor-default disabled:opacity-50 bg-transparent ring-1 text-slate-700 ring-slate-200 hover:bg-slate-50")[Span.Class("me-1").Attributes(("aria-hidden", "true"))["⌥"], "GitHub"]
            ]
        ],
        Main.Class("mx-auto w-full max-w-6xl px-4 py-4")[Router]
    ];
}
