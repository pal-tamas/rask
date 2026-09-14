namespace Rask.Site;

/// <summary>
/// The "up and running in one command" install block — a stateful Rask component. Clicking a tab
/// sets <c>_active</c> and re-renders the selected terminal; no JS, no hidden-toggling.
/// </summary>
public sealed partial class InstallTabs : Component
{
    private static readonly string[] Labels = ["Server", "WASM"];

    private int _active; // 0 = Server, 1 = WASM

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
            Div
                .Class("mb-3 flex justify-center gap-2")
                .Role("tablist")
                .Aria(new Dictionary<string, string?> { ["label"] = "Project template" })[
                Tab(0), Tab(1)
            ],
            // .term and .install-foot are TEST contracts: SiteExampleTests reads the rendered command and
            // the Windows one-liner out of them, and a locator that resolves to nothing fails by timing
            // out rather than by naming what moved. The class rides on the mockup itself now — the
            // wrapper it used to sit on drew a second frame around a component that has its own.
            Terminal(),
            P.Class("install-foot mt-4 text-center text-xs text-ui-muted")[
                "Nothing preinstalled — it adds the .NET SDK too, under ", Code["$HOME"],
                ", no ", Code["sudo"], ". Windows: ", Code[WindowsInstallCommand], "."
            ],
            P.Class("install-foot mt-4 text-center text-xs text-ui-muted")[
                "Accounts and cookie sign-in are already on · full path in the ",
                A
                    .Class("text-ui-brand-ink no-underline hover:underline")
                    .Href("https://github.com/pal-tamas/rask/blob/main/docs/getting-started.md")
                    .Target("_blank")
                    .Rel("noopener")["getting-started guide"], "."
            ]
        ];

    private Component Tab(int i) =>
        Button
            .Key(i)
            // min-h-11 below sm: 44px is the smallest reliable touch target, and these are text-sm.
            .Class("inline-flex min-h-11 items-center rounded-lg px-4 text-sm sm:min-h-0 sm:py-1.5 " + (i == _active
                ? "border border-ui-line bg-ui-bg font-medium text-ui-ink"
                : "border border-transparent text-ui-muted hover:bg-ui-well hover:text-ui-ink"))
            .Type("button")
            .Role("tab")
            .Aria(new Dictionary<string, string?> { ["selected"] = i == _active ? "true" : "false" })
            .OnClick(() => _active = i)[Labels[i]];

    /// <summary>
    ///     The kit's own terminal, rather than a hand-rolled one.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The prompt is what makes this worth the component. It used to be a
    ///         <c>&lt;span class="select-none"&gt;</c>, which stops a drag-select picking it up in most
    ///         browsers and does nothing about "select all", a screen reader, or anything that reads
    ///         <c>textContent</c>. <c>UiMockupCode</c> draws it from <c>data-prefix</c> as a CSS
    ///         pseudo-element — it is not in the document at all, so a copied command is a command.
    ///         That matters here more than anywhere else on the site: this block exists to be pasted
    ///         into a shell, and <c>$ curl …</c> is not a valid one.
    ///     </para>
    ///     <para>
    ///         Both terminals lead with the installer. The tabs pick a TEMPLATE, not an install method,
    ///         and a visitor who lands on the WASM tab still needs the CLI first.
    ///     </para>
    /// </remarks>
    private Component Terminal() =>
        UiMockupCode.Lines(_active == 1 ? WasmLines : ServerLines).Class("term text-left");

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
