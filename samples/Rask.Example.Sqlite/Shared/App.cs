namespace Rask.Example.Sqlite;

// App shell: the framework-managed <head> (Bootstrap + icons via CDN), a navbar, and a Router that
// renders the matched page. Like every component here it renders into <body> — the document around
// it is the framework's (RASK021).
public sealed partial class App : Component
{
    protected override Component? Head =>
    [
        Title["Rask — SQLite production pragmas"],
        Meta.Charset("utf-8"),
        Meta.Name("viewport").Content("width=device-width, initial-scale=1"),
        Link
            .Rel("stylesheet")
            .Href("https://cdn.jsdelivr.net/npm/bootstrap@5.3.3/dist/css/bootstrap.min.css"),
        Link
            .Rel("stylesheet")
            .Href("https://cdn.jsdelivr.net/npm/bootstrap-icons@1.11.3/font/bootstrap-icons.min.css")
    ];

    protected override string? BodyClass => "bg-body-tertiary";

    protected override Component? Render() =>
    [
        Nav.Class("navbar navbar-dark bg-dark border-bottom shadow-sm")[
            Div.Class("container")[
                Span.Class("navbar-brand fw-bold")[
                    I.Class("bi bi-database-check me-2"), "Rask · SQLite pragmas"
                ],
                A
                    .Href("https://github.com/pal-tamas/rask")
                    .Target("_blank")
                    .Class("btn btn-outline-light btn-sm")[I.Class("bi bi-github me-1"), "GitHub"]
            ]
        ],
        Main.Class("container py-4")[Router]
    ];
}
