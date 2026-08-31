namespace Rask.Example.Site;

/// <summary>
/// The "up and running in one command" install block — a stateful Rask component. Clicking a tab
/// sets <c>_active</c> and re-renders the selected terminal; no JS, no hidden-toggling.
/// </summary>
public sealed partial class InstallTabs : Component
{
    private static readonly string[] Labels = ["Server", "WASM"];

    private int _active; // 0 = Server, 1 = WASM

    /// <inheritdoc />
    protected override Component? Render() =>
        Div.Class("mx-auto max-w-2xl")[
            Div
                .Class("mb-3 flex justify-center gap-2")
                .Role("tablist")
                .Aria(new Dictionary<string, string?> { ["label"] = "Project template" })[
                Tab(0), Tab(1)
            ],
            // .term is a TEST contract: SiteExampleTests reads the rendered command out of it, and a
            // locator that resolves to nothing fails by timing out rather than by naming what moved.
            Div.Class("term overflow-x-auto rounded-2xl border border-line bg-panel-2 p-5 text-left")[
                Terminal()
            ],
            P.Class("mt-4 text-center text-xs text-slate-500 dark:text-slate-400")[
                "Add ", Code["--auth"], " for a cookie/JWT starter · full path in the ",
                A
                    .Class("text-accent-ink no-underline hover:underline")
                    .Href("https://github.com/pal-tamas/rask/blob/main/docs/getting-started.md")
                    .Target("_blank")
                    .Rel("noopener")["getting-started guide"], "."
            ]
        ];

    private static Component Line(string prompt, string rest) =>
        [Span.Class("select-none text-accent-ink")[prompt], rest + "\n"];

    private Component Tab(int i) =>
        Button
            .Key(i)
            .Class(i == _active
                ? "rounded-lg border border-line bg-panel px-4 py-1.5 text-sm font-medium text-ink"
                : "rounded-lg border border-transparent px-4 py-1.5 text-sm text-slate-500 dark:text-slate-400 hover:text-ink")
            .Type("button")
            .Role("tab")
            .Aria(new Dictionary<string, string?> { ["selected"] = i == _active ? "true" : "false" })
            .OnClick(() => _active = i)[Labels[i]];

    private Component Terminal() => _active switch
    {
        1 => Pre.Class("font-mono text-xs leading-relaxed text-ink-soft")[Code[
            Span.Class("text-slate-500 dark:text-slate-400")["# standalone browser-WASM SPA, installable and offline\n"],
            Line("$", " dotnet tool install -g Rask.Cli"),
            Line("$", " rask new MyApp --template wasm"),
            Span.Class("select-none text-accent-ink")["$"], " cd MyApp && rask dev"
        ]],
        _ => Pre.Class("font-mono text-xs leading-relaxed text-ink-soft")[Code[
            Span.Class("text-slate-500 dark:text-slate-400")["# ASP.NET live-server app, batteries included\n"],
            Line("$", " dotnet tool install -g Rask.Cli"),
            Line("$", " rask new MyApp"),
            Span.Class("select-none text-accent-ink")["$"], " cd MyApp && rask dev"
        ]]
    };
}
