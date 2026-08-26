namespace Rask.Example.Site;

/// <summary>
/// The "up and running in one command" install block — a stateful Rask component. Clicking a tab
/// sets <c>_active</c> and re-renders the selected terminal; no JS, no hidden-toggling.
/// </summary>
public sealed partial class InstallTabs : Component
{
    private int _active; // 0 = Server, 1 = WASM

    private static readonly string[] Labels = ["Server", "WASM"];

    private Component Tab(int i) =>
        Button
            .Class("tab")
            .Type("button")
            .Role("tab")
            .Aria(new Dictionary<string, string?> { ["selected"] = i == _active ? "true" : "false" })
            .OnClick(() => _active = i)[Labels[i]];

    private static Component Line(string prompt, string rest) =>
        [Span.Class("prompt")[prompt], rest + "\n"];

    private Component Terminal() => _active switch
    {
        1 => Pre[Code[
            Span.Class("cmt")["# standalone browser-WASM SPA, installable and offline\n"],
            Line("$", " dotnet tool install -g Rask.Cli"),
            Line("$", " rask new MyApp --template wasm"),
            Span.Class("prompt")["$"], " cd MyApp && rask dev"
        ]],
        _ => Pre[Code[
            Span.Class("cmt")["# ASP.NET live-server app, batteries included\n"],
            Line("$", " dotnet tool install -g Rask.Cli"),
            Line("$", " rask new MyApp"),
            Span.Class("prompt")["$"], " cd MyApp && rask dev"
        ]]
    };

    protected override Component? Render() =>
        Div.Class("install-wrap")[
            Div
                .Class("tabs")
                .Role("tablist")
                .Aria(new Dictionary<string, string?> { ["label"] = "Project template" })[
                Tab(0), Tab(1)
            ],
            Div.Class("term")[Terminal()],
            P.Class("install-foot")[
                "Add ", Code["--auth"], " for a cookie/JWT starter · full path in the ",
                A
                    .Href("https://github.com/pal-tamas/rask/blob/main/docs/getting-started.md")
                    .Target("_blank")
                    .Rel("noopener")["getting-started guide"], "."
            ]
        ];
}
