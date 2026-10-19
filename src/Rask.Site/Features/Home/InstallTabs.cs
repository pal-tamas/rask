namespace Rask.Site;

/// <summary>
/// The "up and running in one command" install block — a stateful Rask component. Choosing a tab
/// sets <c>_template</c> and re-renders the selected terminal; no hidden-toggling.
/// </summary>
public sealed partial class InstallTabs : Component
{
    private string _template = "server";

    /// <summary>
    /// The install command, spelled once. It is the same string in the README, NUGET.md, docs/cli.md,
    /// docs/getting-started.md, docs/installation.md, the tutorial and llms.txt, and
    /// <c>scripts/tests/install-script.test.sh</c> fails the build if any of them drifts — a wrong URL
    /// on the landing page is a broken front door that nothing else would catch.
    /// </summary>
    private const string InstallCommand = "curl -sSL https://rask.sh/rask.sh | sh";

    private const string WindowsInstallCommand = "irm https://rask.sh/rask.ps1 | iex";

    /// <inheritdoc />
    protected override Component? Render() =>
        Div.Class("mx-auto max-w-2xl")[
            // Flux's segmented tabs: a choice the page reads from OnChange, with no panels beside it.
            Div.Class("mb-3 flex justify-center")[
                Ui.Tabs.Segmented.Value(_template).OnChange(name => _template = name)[
                    Ui.Tab.Name("server")["Server"],
                    Ui.Tab.Name("wasm")["WASM"]
                ]
            ],
            // .term and .install-foot are TEST contracts: SiteExampleTests reads the rendered command and
            // the Windows one-liner out of them, and a locator that resolves to nothing fails by timing
            // out rather than by naming what moved.
            Terminal(),
            P.Class("install-foot mt-4 text-center text-xs text-ui-muted")[
                "Nothing preinstalled — it adds the .NET SDK too, under ", Code["$HOME"],
                ", no ", Code["sudo"], ". Windows: ", Code[WindowsInstallCommand], "."
            ],
            P.Class("install-foot mt-4 text-center text-xs text-ui-muted")[
                "Accounts and cookie sign-in are already on · full path in the ",
                // The on-site guide, through the typed route like every other card on the page: it
                // navigates inside the app instead of leaving for GitHub's raw markdown in a new tab.
                NavLink
                    .Href(PageMeta.LinkTo(Routes.GuidePage("getting-started")))
                    .ActiveClass("")
                    .Class("text-ui-brand-ink no-underline hover:underline")["getting-started guide"], "."
            ]
        ];

    /// <summary>
    ///     A terminal, drawn as a code block on Flux's docs is: dark, rounded, monospace.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The prompt is the line's <c>::before</c>, drawn from <c>data-prefix</c>: it is not in the document at
    ///         all, so "select all", a screen reader and <c>textContent</c> never pick it up and a copied command is
    ///         a command. This block exists to be pasted into a shell, and <c>$ curl …</c> is not a valid one. The
    ///         prompt's box is wider than the character, which is the gap between it and the command.
    ///     </para>
    ///     <para>
    ///         It scrolls sideways rather than wrapping: a wrapped shell command is ambiguous about where it ends,
    ///         and on a phone the installer's URL is wider than the screen.
    ///     </para>
    ///     <para>
    ///         Both terminals lead with the installer. The tabs pick a TEMPLATE, not an install method,
    ///         and a visitor who lands on the WASM tab still needs the CLI first.
    ///     </para>
    /// </remarks>
    private Component Terminal() =>
        Div.Class("term overflow-x-auto rounded-xl bg-zinc-900 py-4 text-left font-mono text-sm leading-7 text-zinc-100 shadow-xs ring-1 ring-zinc-900/10 dark:bg-zinc-800 dark:ring-white/10")[
            (string.Equals(_template, "wasm", StringComparison.Ordinal) ? WasmLines : ServerLines).Select((line, index) =>
                Pre
                    .Key(index)
                    .Class("m-0 pe-5 ps-4 before:inline-block before:w-9 before:text-zinc-500 before:content-[attr(data-prefix)]")
                    .Attributes(("data-prefix", line.Prefix))[Code[line.Text]])
        ];

    private static readonly (string Prefix, string Text)[] ServerLines =
    [
        ("#", "ASP.NET live-server app, batteries included"),
        ("$", InstallCommand),
        ("$", "rask new MyApp"),
        ("$", "cd MyApp && rask dev"),
    ];

    private static readonly (string Prefix, string Text)[] WasmLines =
    [
        ("#", "standalone browser-WASM SPA, installable and offline"),
        ("$", InstallCommand),
        ("$", "rask new MyApp --template wasm"),
        ("$", "cd MyApp && rask dev"),
    ];
}
