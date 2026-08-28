namespace Rask.Example.Site;

/// <summary>
/// The "up and running in one command" install block — a stateful Rask component. Clicking a tab
/// sets <c>_active</c> and re-renders the selected terminal; no JS, no hidden-toggling.
/// </summary>
public sealed partial class InstallTabs : Component
{
    private int _active; // 0 = Server, 1 = WASM

    private static readonly string[] Labels = ["Server", "WASM"];

    /// <summary>
    /// The install command, spelled once. It is the same string in the README, NUGET.md, docs/cli.md,
    /// docs/getting-started.md, docs/installation.md, the tutorial and llms.txt, and
    /// <c>scripts/tests/install-script.test.sh</c> fails the build if any of them drifts — a wrong URL
    /// on the landing page is a broken front door that nothing else would catch.
    /// </summary>
    private const string InstallCommand = "curl -sSL https://pal-tamas.github.io/rask/rask.sh | sh";

    private const string WindowsInstallCommand = "irm https://pal-tamas.github.io/rask/rask.ps1 | iex";

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
            Line("$", " " + InstallCommand),
            Line("$", " rask new MyApp --template wasm"),
            Span.Class("prompt")["$"], " cd MyApp && rask dev"
        ]],
        _ => Pre[Code[
            Span.Class("cmt")["# ASP.NET live-server app, batteries included\n"],
            Line("$", " " + InstallCommand),
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
                "Nothing preinstalled — it adds the .NET 10 SDK too, under ", Code["$HOME"],
                ", no ", Code["sudo"], ". Windows: ", Code[WindowsInstallCommand], "."
            ],
            P.Class("install-foot")[
                "Add ", Code["--auth"], " for a cookie/JWT starter · full path in the ",
                A
                    .Href("https://github.com/pal-tamas/rask/blob/main/docs/getting-started.md")
                    .Target("_blank")
                    .Rel("noopener")["getting-started guide"], "."
            ]
        ];
}
