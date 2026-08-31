namespace Rask.Example.Sqlite;

// App shell: the framework-managed <head> (Bootstrap + icons via CDN), a navbar, and a Router that
// renders the matched page. Like every component here it renders into <body> — the document around
// it is the framework's (RASK021).
public sealed partial class App : Component
{
    protected override Component? HeadAssets =>
    [
        Title["Rask — SQLite production pragmas"],
        Meta.Charset("utf-8"),
        Meta.Name("viewport").Content("width=device-width, initial-scale=1"),
        Link.Rel("stylesheet").Href("/css/app.css")
    ];

    protected override string? BodyClass => "bg-slate-50 dark:bg-slate-900";

    protected override Component? Render() =>
    [
        Nav.Class("flex items-center border-b border-slate-800 bg-slate-900 text-slate-100 shadow-sm")[
            Div.Class("mx-auto w-full max-w-6xl px-4")[
                Span.Class("app-brand font-bold")[
                    Span.Class("me-2").Attributes(("aria-hidden", "true"))["🗄"], "Rask · SQLite pragmas"
                ],
                A
                    .Href("https://github.com/pal-tamas/rask")
                    .Target("_blank")
                    .Class("inline-flex items-center gap-1.5 rounded-md px-2.5 py-1.5 text-sm font-medium no-underline transition disabled:cursor-default disabled:opacity-50 bg-transparent ring-1 text-slate-700 ring-slate-200 hover:bg-slate-50")[Span.Class("me-1").Attributes(("aria-hidden", "true"))["⌥"], "GitHub"]
            ]
        ],
        Main.Class("mx-auto w-full max-w-6xl px-4 py-4")[Router]
    ];
}
