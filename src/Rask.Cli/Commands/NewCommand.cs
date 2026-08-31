using System.Globalization;
using Rask.Cli.Scaffolding;
using Rask.Cli.Templates;
using Spectre.Console;

namespace Rask.Cli.Commands;

/// <summary>
/// <c>rask new</c> — scaffold a Rask project. The CLI is the scaffolding authority: every template
/// (<c>server</c>, <c>wasm</c>, and the front-end ones) is generated directly — files written +
/// package refs baked at the CLI's own version + <c>dotnet restore</c> — with no <c>dotnet new</c> /
/// Rask.Templates dependency.
/// </summary>
internal sealed class NewCommand(IConsole console, IFileSystem fileSystem, IProcessRunner process, string workingDirectory)
    : CliCommand(console)
{
    private readonly IFileSystem _fileSystem = fileSystem;
    private readonly IProcessRunner _process = process;
    private readonly string _workingDirectory = workingDirectory;

    /// <summary>The opt-in feature flags <c>rask new</c> forwards to a template (as <c>--flag</c>).</summary>
    /// <summary>Every template-scoped flag <c>rask new</c> understands — the batteries plus <c>auth</c>.</summary>
    internal static readonly string[] FeatureFlags =
    [
        "auth", "wasm", "pwa", "cqrs", "data", "docker",
        "jobs", "mail", "cache", "outbox", "push", "snapshots", "logs", "ops",
    ];

    /// <summary>
    /// The batteries — everything a template supports <em>except</em> auth, and therefore exactly what a
    /// bare <c>rask new</c> turns on.
    /// </summary>
    /// <remarks>
    /// Derived from <see cref="FeatureFlags"/> minus <c>auth</c> rather than listed a second time. The
    /// default set is then <c>template.SupportedFlags</c> intersected with this, so a template that cannot
    /// host a database gets the right answer without anyone maintaining a per-template default list — the
    /// same reasoning that keeps <see cref="TemplateCatalog"/> derived from <see cref="SpaFramework.All"/>.
    ///
    /// <para>
    /// Auth and wasm are the two left off, because they are the ones that change what the app <em>is</em>
    /// rather than what it can do. A login wall in front of a project you are about to show someone is a
    /// decision, not a convenience; and shipping a browser bundle makes every publish link a WebAssembly
    /// runtime and starts moving pages off the server. Styling is not a decision: Tailwind is built in.
    /// </para>
    /// </remarks>
    internal static readonly string[] BatteryFlags =
        [.. FeatureFlags.Where(f => f is not ("auth" or "wasm"))];

    /// <summary>The <c>--no-*</c> spelling of a battery.</summary>
    internal static string OffFlag(string battery) => "no-" + battery;


    public override string Name => "new";

    public override string Summary => "Create a new Rask project from a template.";

    // The shape only — the flags are listed once, in the schema below, which --help renders directly.
    public override string Usage => "rask new <name> [options]";

    public override IReadOnlyList<(string Name, string Description)> Arguments =>
        [("<name>", "Name of the project to create (scaffolds ./<name>/).")];

    public override IReadOnlyList<string> Examples =>
    [
        "rask new Shop",
        "rask new Shop --auth --data",
        "rask new Shop --wasm",
        "rask new Shop --template wasm",
        "rask new Blog --no-push --no-ops",
        "rask new Tiny --no-data --no-docker --no-pwa",
    ];

    public override ArgumentSchema? OptionSchema => CreateSchema();

    /// <summary>The flag/option schema — shared by <see cref="ExecuteAsync"/> and <c>--help</c> so they can't drift.</summary>
    private static ArgumentSchema CreateSchema() =>
        new ArgumentSchema()
            .Option("template", 't', "name", "Template to scaffold (default: server).", choices: TemplateCatalog.Keys)
            .Option("output", 'o', "dir", "Directory to create the project in (default: ./<name>).")
            .Option("name", 'n', "name", "Project name, if not given positionally.")
            .Flag("auth", description: "Add cookie authentication (login + members pages). Off by default, like --wasm.")
            .Flag("wasm", description: "Also publish a browser bundle from this project, so an eligible page moves into WebAssembly once it has downloaded. Publish takes minutes longer; `dotnet run` is unaffected.")
            .Flag("no-pwa", description: "Leave out the PWA manifest, icon, and offline page (also drops Web Push).")
            .Flag("no-push", description: "Leave out server-sent Web Push and its subscribe endpoints.")
            .Flag("no-cqrs", description: "Leave out Rask.Cqrs — and with it the database, which every scaffolded feature dispatches through.")
            .Flag("no-data", description: "Leave out the database and EF Core — and with it every battery that maps onto a DbContext.")
            .Flag("no-jobs", description: "Leave out durable background jobs.")
            .Flag("no-mail", description: "Leave out transactional email.")
            .Flag("no-cache", description: "Leave out the database-backed ICache + IDistributedCache.")
            .Flag("no-outbox", description: "Leave out the transactional outbox for durable domain events.")
            .Flag("no-snapshots", description: "Leave out scheduled point-in-time SQLite backups.")
            .Flag("no-logs", description: "Leave out the durable log store (it keeps a database of its own).")
            .Flag("no-ops", description: "Leave out the operator dashboard at /_rask.")
            .Flag("no-docker", description: "Leave out the Dockerfile and .dockerignore.")
            .Flag("no-restore", description: "Don't run dotnet restore after scaffolding (for offline use). Also skips the first migration.")
            .Flag("no-git", description: "Don't initialize a git repository (one is created with an initial commit by default).")
            .Flag("force", description: "Scaffold into a directory that already has files in it, overwriting on collision.")
            .Flag("dry-run", description: "Print the files that would be written without touching disk.");

    public override Task<int> ExecuteAsync(IReadOnlyList<string> args, CancellationToken cancellationToken) =>
        ExecuteAsync(args, allowWizard: true, cancellationToken);

    private async Task<int> ExecuteAsync(IReadOnlyList<string> args, bool allowWizard, CancellationToken cancellationToken)
    {
        var schema = CreateSchema();

        // Before the parse, so a retired flag gets its own answer rather than the generic did-you-mean the
        // unknown-option path would offer. These were real flags in the last release and are all over the
        // internet; "unknown option --data" would read as a broken CLI rather than as a changed default.
        if (RetiredFlagError(args) is { } retired)
        {
            return Fail(retired);
        }

        var parsed = schema.Parse(args);
        if (parsed.HasErrors)
        {
            return Fail(parsed.Errors);
        }

        // A second positional is almost always an unquoted multi-word name — `rask new My App`. Taking the
        // first and dropping the rest would scaffold a project called "My" and say nothing, which is the
        // worst outcome available: silent, wrong, and only noticed after the files are on disk. Every other
        // command in this CLI rejects a stray positional; this one used to be the exception.
        if (parsed.Positionals.Count > 1)
        {
            var joined = string.Concat(parsed.Positionals);
            return Fail(
                $"'rask new' takes one project name, but got {parsed.Positionals.Count.ToString(CultureInfo.InvariantCulture)}: "
                + $"{string.Join(", ", parsed.Positionals.Select(p => $"'{p}'"))}. "
                + (Identifiers.IsValidNamespaceName(joined)
                    ? $"A project name can't contain spaces — did you mean '{joined}'?"
                    : "A project name can't contain spaces."));
        }

        // Both spellings of the same answer, disagreeing. Preferring one silently means the command did
        // something the user can read the opposite of straight off their own command line.
        if (parsed.Option("name") is { } named
            && parsed.Positionals.FirstOrDefault() is { } positional
            && !named.Equals(positional, StringComparison.Ordinal))
        {
            return Fail($"Two different project names given: '{positional}' and --name '{named}'. Pass one.");
        }

        var name = parsed.Option("name") ?? parsed.Positionals.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(name))
        {
            // No name given. On a terminal, walk an interactive wizard and re-run with the answers
            // (allowWizard:false bounds this to one hop); piped/scripted, keep the hard-error contract.
            var prompt = new Prompt(Console);
            if (allowWizard && prompt.Interactive)
            {
                return await ExecuteAsync(RunWizard(prompt, args, parsed), allowWizard: false, cancellationToken).ConfigureAwait(false);
            }

            return Fail("A project name is required, e.g. 'rask new Shop'.");
        }

        if (ValidateOutput(parsed.Option("output")) is { } outputError)
        {
            return Fail(outputError);
        }

        // The name becomes the root namespace and the csproj filename, so an invalid one (a dash, a leading
        // digit, a keyword) would scaffold a project that never compiles. Reject it up front with guidance
        // rather than writing files the user then has to throw away.
        if (!Identifiers.IsValidNamespaceName(name))
        {
            return Fail(
                $"'{name}' isn't a valid project name — it becomes the root namespace, so each dot-separated part must "
                + "start with a letter or underscore and contain only letters, digits, or underscores (e.g. Shop or Contoso.Shop).");
        }

        // The schema declares TemplateCatalog.Keys as this option's choices, so the parse already rejected
        // (and reported) anything else — the lookup here can only succeed.
        var templateKey = parsed.Option("template") ?? TemplateCatalog.Default.Key;
        _ = TemplateCatalog.TryGet(templateKey, out var template);

        // Everything the template can do is on unless it was turned off, so the only per-battery input is
        // the --no-* set. Auth is the exception in both directions: off by default, and asked for by name.
        var off = BatteryFlags.Where(flag => parsed.HasFlag(OffFlag(flag))).ToArray();
        var auth = parsed.HasFlag("auth");
        var wasm = parsed.HasFlag("wasm");

        // Turning off something this template never had is a mistake worth naming: it means the command
        // line was written against a different template, and silently accepting it would hide that.
        var absent = off.Where(flag => !template.SupportedFlags.Contains(flag))
            .Select(OffFlag)
            .Concat(auth && !template.SupportedFlags.Contains("auth") ? ["auth"] : Array.Empty<string>())
            .ToArray();
        if (absent.Length > 0)
        {
            var supported = template.SupportedFlags.Count == 0
                ? "(none)"
                : string.Join(", ", template.SupportedFlags.OrderBy(f => f, StringComparer.Ordinal));
            var rejected = string.Join(", ", absent.Select(f => "--" + f));
            return Fail(
                $"Template '{template.Key}' has nothing to change for: {rejected}. It supports: {supported}.");
        }

        // The generated TypeScript contracts ARE the mediator's wire on these templates, so there is no
        // project left without it. Refused rather than ignored, for the same reason --tailwind is below:
        // a flag the CLI accepts and then disregards is the most expensive kind to discover.
        if (off.Contains("cqrs") && SpaFramework.TryGet(template.Key, out _))
        {
            return Fail(
                $"Template '{template.Key}' can't drop CQRS — the generated TypeScript client dispatches "
                + "through it, so it is the template rather than a battery in it.");
        }

        var batteries = ToBatteries(template, off, auth, wasm);

        // Every template is generated directly by the CLI; the key here is one the catalog knows
        // (validated by TemplateCatalog.TryGet).
        return await GenerateDirectAsync(
            template, name, parsed.Option("output"), parsed.HasFlag("dry-run"), parsed.HasFlag("force"),
            parsed.HasFlag("no-restore"), parsed.HasFlag("no-git"), batteries,
            (dir, version) =>
            {
                // A front-end framework claims its own template key, so this has to be asked before the
                // switch below — and asking the SAME list the catalog was built from is what stops a key
                // being accepted by the parser and then generating something else.
                if (SpaFramework.TryGet(template.Key, out var framework))
                {
                    return ProjectGenerator.GenerateSpa(dir, name, framework, batteries, version);
                }

                return template.Key switch
                {
                    "wasm" => ProjectGenerator.GenerateWasm(
                        dir, name, batteries.Auth, batteries.Pwa, batteries.Docker, version, batteries),
                    _ => ProjectGenerator.GenerateServer(dir, name, batteries, version),
                };
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The flags that used to turn a battery <em>on</em>, and are gone now that every battery is on by
    /// default — mapped to the answer that says so.
    /// </summary>
    /// <remarks>
    /// Rejected with a targeted message rather than kept as a silent no-op. A flag the CLI accepts and
    /// then disregards is this repository's most expensive bug class — it is what <c>--template native</c>
    /// did, and the reason <c>--tailwind</c> is refused on the WASM templates instead of ignored.
    /// </remarks>
    private static string? RetiredFlagError(IReadOnlyList<string> args)
    {
        foreach (var arg in args)
        {
            // `--flag=false` is a spelling the parser accepts, so match the prefix too rather than only
            // the bare token.
            var name = arg.StartsWith("--", StringComparison.Ordinal)
                ? arg[2..].Split('=')[0]
                : null;

            if (name is null)
            {
                continue;
            }

            // Both named an answer on an axis that no longer exists. Tailwind is not an option a project
            // picks, it is what a Rask project is styled with — so there is nothing left for either flag
            // to mean, and someone's muscle memory still has them in it.
            if (name.Equals("tailwind", StringComparison.Ordinal))
            {
                return "--tailwind is gone: Tailwind is built in, so every project is scaffolded with it.";
            }

            if (name.Equals("bootstrap", StringComparison.Ordinal))
            {
                return "--bootstrap is gone: Rask.Bootstrap has been removed and every project is styled "
                    + "with Tailwind, which is built in.";
            }

            if (name.Equals("all-batteries", StringComparison.Ordinal))
            {
                return "--all-batteries is gone: every battery is on by default now. "
                    + "Pass --no-<battery> to leave one out, e.g. --no-push.";
            }

            // Ahead of the general case, which would say "on by default now" and send the reader looking
            // for a --no- that no longer exists either.
            if (name.Equals("localization", StringComparison.Ordinal)
                || name.Equals("no-localization", StringComparison.Ordinal)
                || name.Equals("culture", StringComparison.Ordinal))
            {
                return $"--{name} is gone: the languages an app ships are configured in Program.cs, not on "
                    + "this command line. A new project starts with English, and adding a language is a "
                    + "line in the AddRask(configureCulture: ...) call it already has — see "
                    + "docs/localization.md.";
            }

            if (Array.IndexOf(BatteryFlags, name) >= 0)
            {
                return $"--{name} is on by default now, so there is nothing to turn on. "
                    + $"Pass --no-{name} to leave it out.";
            }
        }

        return null;
    }

    /// <summary>
    /// The batteries a project gets: everything <paramref name="template"/> supports, minus whatever
    /// <paramref name="off"/> names, with auth asked for separately.
    /// </summary>
    /// <remarks>
    /// <b>This is where "batteries included" is decided</b>, and it is decided here rather than on
    /// <see cref="ServerBatteries"/> because this is the only layer that knows the template. A default set
    /// baked into the record would have to be the same for a server app and a browser-WASM SPA, which have
    /// almost nothing in common, and would silently change what every generator test means.
    ///
    /// <para>
    /// Deriving the set from <c>template.SupportedFlags</c> means there is no per-template default list to
    /// maintain: a template that cannot host a database does not advertise <c>data</c>, so it does not get
    /// one. Adding a battery to a template's flag set is all it takes to put it on the golden path.
    /// </para>
    ///
    /// <para>
    /// <see cref="ServerBatteries.Reduced"/> runs before <see cref="ServerBatteries.Normalized"/>, and the
    /// order is load-bearing — normalizing first would turn <c>Data</c> back on for any pillar still
    /// standing and undo every <c>--no-*</c> the user typed.
    /// </para>
    /// </remarks>
    /// <summary>
    /// A battery set with exactly the named batteries on, and nothing else.
    /// </summary>
    /// <remarks>
    /// A different question from <see cref="ToBatteries"/>, which answers "what did the user ask for on
    /// this template". This one answers "give me precisely this combination", which is what the scaffold
    /// tests, the tutorial gate and the showcase sample's provenance check need — they are pinning one
    /// shape of generated code, not the CLI's defaults, and would otherwise all change every time a
    /// template gains a battery.
    ///
    /// <para>
    /// Deliberately not normalized: a caller asking for exactly this wants exactly this, and every
    /// generator normalizes on the way in anyway.
    /// </para>
    /// </remarks>
    internal static ServerBatteries BatteriesOf(
        IReadOnlyCollection<string> on) =>
        new()
        {
            Localization = on.Contains("localization"),
            Auth = on.Contains("auth"),
            Wasm = on.Contains("wasm"),
            Pwa = on.Contains("pwa"),
            Cqrs = on.Contains("cqrs"),
            Data = on.Contains("data"),
            Docker = on.Contains("docker"),
            Jobs = on.Contains("jobs"),
            Mail = on.Contains("mail"),
            Cache = on.Contains("cache"),
            Outbox = on.Contains("outbox"),
            Push = on.Contains("push"),
            Snapshots = on.Contains("snapshots"),
            Logs = on.Contains("logs"),
            Ops = on.Contains("ops"),
        };

    internal static ServerBatteries ToBatteries(
        TemplateInfo template,
        IReadOnlyCollection<string> off,
        bool auth = false,
        bool wasm = false)
    {
        // Every battery a template supports is on unless it was turned off. There is no longer an
        // opt-in exception: localization was the only one, and it is not a flag any more (#854).
        bool On(string battery) =>
            template.SupportedFlags.Contains(battery) && !off.Contains(battery);

        return new ServerBatteries
        {
            Auth = auth,
            // Like auth: asked for by name, and only honoured by a template that can host it.
            Wasm = wasm && template.SupportedFlags.Contains("wasm"),
            // Not a flag any more (#854): the languages an app ships are configured in Program.cs, so
            // this is only "does this template scaffold the registration at all". CultureList stays empty
            // here — Normalized() fills in "en", the default a scaffolded app starts from and edits.
            Localization = template.ShipsLocalization,
            Pwa = On("pwa"),
            Cqrs = On("cqrs"),
            Data = On("data"),
            Docker = On("docker"),
            Jobs = On("jobs"),
            Mail = On("mail"),
            Cache = On("cache"),
            Outbox = On("outbox"),
            Push = On("push"),
            Snapshots = On("snapshots"),
            Logs = On("logs"),
            Ops = On("ops"),
        }.Reduced().Normalized();
    }

    /// <summary>
    /// Walk the interactive first-run flow (name → template → batteries) and return the
    /// equivalent argument list, so the answers flow back through the exact same validation and generation
    /// path as a fully-typed command line. Only reached on a terminal.
    /// <para>
    /// The wizard <b>fills gaps, it does not re-ask</b>: whatever the command line already answered is
    /// kept verbatim and its question is skipped, so <c>rask new --template wasm</c> asks for a name and
    /// nothing else. Questions are also skipped when they cannot apply — no snapshots question on a
    /// template with no database.
    /// </para>
    /// </summary>
    private IReadOnlyList<string> RunWizard(Prompt prompt, IReadOnlyList<string> args, ParsedArguments parsed)
    {
        Branding.Write(Console, "let's set up your project");
        var ansi = Console.Ansi;
        ansi.Write(new Rule().RuleStyle("dim"));
        ansi.WriteLine();

        // Everything already typed stands; the wizard only appends what is still unanswered.
        var filled = new List<string>(args);

        if (string.IsNullOrWhiteSpace(parsed.Option("name") ?? parsed.Positionals.FirstOrDefault()))
        {
            // Validate here rather than after the answers are re-parsed: being told the name is unusable
            // while still in the question is a correction, being told it afterwards is a restart.
            filled.Insert(0, prompt.Ask(
                "Project name",
                validate: value => Identifiers.IsValidNamespaceName(value)
                    ? null
                    : $"'{value}' can't be a root namespace — start each dot-separated part with a letter or underscore (e.g. Shop or Contoso.Shop)."));
        }

        var templateKey = parsed.Option("template");
        if (templateKey is null)
        {
            templateKey = prompt.Select(
                "Project type",
                [.. TemplateCatalog.All.Select(t => (t.Key, $"[bold]{t.Key}[/] [dim]— {t.DisplayName}[/]"))],
                TemplateCatalog.Default.Key);

            filled.Add("--template");
            filled.Add(templateKey);
        }

        _ = TemplateCatalog.TryGet(templateKey, out var template);


        // Styling asks nothing: Tailwind is built in, so there is no answer a project could give.

        // Auth and the browser rung get questions of their own because they are the two that are off by
        // default. Everything else on the list is already on, so the checklist below is about taking
        // things away; mixing the things you ADD into a list of things you remove would read as the
        // opposite of what it does.
        if (!parsed.HasFlag("auth") && template.SupportedFlags.Contains("auth")
            && prompt.Confirm("Add [bold]authentication[/] — login, sessions, members pages?", @default: false))
        {
            filled.Add("--auth");
        }

        // The cost is named in the question. It is the one answer here that makes every later publish
        // minutes slower, and finding that out afterwards is worse than being asked.
        if (!parsed.HasFlag("wasm") && template.SupportedFlags.Contains("wasm")
            && prompt.Confirm(
                "Also run pages in the [bold]browser[/] — publishes a WebAssembly bundle (slower publish)?",
                @default: false))
        {
            filled.Add("--wasm");
        }

        // Pre-ticked, because this is what a bare `rask new` already gives you. The question is "anything
        // you don't want?", and unticking an entry becomes the --no-<battery> that says so. A battery this
        // template supports but leaves out is offered UNticked, so the list still shows everything on
        // offer and the checklist stays the one place that says what you are getting.
        // Every offered battery is standard now: localization was the one exception, and it left the
        // command line entirely with #854.
        var offered = BatteryFlags.Where(template.SupportedFlags.Contains).ToArray();
        var standard = offered;

        // A command line that already answered this — either way round — is not re-asked.
        var batteriesGiven = BatteryFlags.Any(f => parsed.HasFlag(OffFlag(f)));
        if (!batteriesGiven && offered.Length > 0)
        {
            var kept = prompt.MultiSelect(
                offered.Length == standard.Length
                    ? "Batteries [dim](all on — space to untick)[/]"
                    : "Batteries [dim](space to tick or untick)[/]",
                [.. offered.Select(f => (f, $"[bold]{f}[/] [dim]— {BatteryDescriptions[f]}[/]"))],
                selected: standard);

            filled.AddRange(standard.Except(kept).Select(f => "--" + OffFlag(f)));
        }

        WriteWizardSummary(filled, template);
        return filled;
    }

    /// <summary>
    /// One line per battery for the wizard's checklist, phrased as what you <em>get</em>.
    /// </summary>
    /// <remarks>
    /// Not the schema's own descriptions: those are written for <c>--no-jobs</c> and so read "leave out
    /// …", which is exactly backwards next to a ticked box. Kept honest by a test that asserts every
    /// <see cref="BatteryFlags"/> entry has an entry here.
    /// </remarks>
    internal static readonly IReadOnlyDictionary<string, string> BatteryDescriptions =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["pwa"] = "installable: a manifest, an icon, and an offline page",
            ["cqrs"] = "the source-generated mediator every feature dispatches through",
            ["data"] = "a SQLite database and an AppDbContext your features map through",
            ["docker"] = "a production Dockerfile and .dockerignore",
            ["jobs"] = "durable background jobs on the app's own database",
            ["mail"] = "transactional email, queued and sent off the request thread",
            ["cache"] = "a database-backed ICache and IDistributedCache",
            ["outbox"] = "a transactional outbox for durable domain events",
            ["push"] = "server-sent Web Push, with the subscribe endpoints",
            ["snapshots"] = "scheduled point-in-time backups of the SQLite file",
            ["logs"] = "a durable log store, so the log survives a restart",
            ["ops"] = "an operator dashboard at /_rask over every battery's table",
        };

    /// <summary>
    /// Restate the answers before the files start appearing, so the scaffolding output is read as the
    /// result of a decision rather than as a wall of paths.
    /// </summary>
    /// <remarks>
    /// Only rows that were actually decided are shown. A summary listing every axis would tell a WASM
    /// SPA it had chosen a database battery — a question that template never asks and does not support —
    /// which is worse than saying nothing, because it reads as confirmation.
    /// </remarks>
    private void WriteWizardSummary(IReadOnlyList<string> args, TemplateInfo template)
    {
        // Resolved through the same path the scaffold will take, rather than read back off the flags. The
        // summary's whole job is to be what happens next, and a second reading of the same answers is how
        // it comes to say something the generator then contradicts.
        var off = BatteryFlags.Where(f => args.Contains("--" + OffFlag(f), StringComparer.Ordinal)).ToArray();
        var batteries = ToBatteries(
            template, off,
            auth: args.Contains("--auth", StringComparer.Ordinal),
            wasm: args.Contains("--wasm", StringComparer.Ordinal));

        var on = BatteryFlags.Where(f => f != "docker" && Includes(batteries, f)).ToArray();

        var grid = new Grid();
        grid.AddColumn(new GridColumn().NoWrap().PadRight(2));
        grid.AddColumn();
        grid.AddRow(Label("📦", "Project"), new Text(args[0]));
        grid.AddRow(Label("🧩", "Type"), new Text(template.DisplayName));

        grid.AddRow(
            Label("🔑", "Auth"),
            new Text(batteries.Auth ? "cookie login + members pages" : "none"));

        if (batteries.Data)
        {
            grid.AddRow(Label("🗄️", "Database"), new Text("SQLite (one file, no server)"));
        }

        grid.AddRow(Label("🐳", "Docker"), new Text(batteries.Docker ? "yes" : "no"));

        grid.AddRow(
            Label("🔋", "Batteries"),
            new Text(on.Length > 0 ? string.Join(", ", on) : "none"));

        var ansi = Console.Ansi;
        ansi.WriteLine();
        ansi.Write(new RaggedRight(new Padder(grid, new Padding(1, 0, 0, 1))));

        Text Label(string emoji, string text) =>
            new(Branding.Label(Console, emoji, text), ConsoleStyling.Of(ConsoleStyle.Dim));
    }

    /// <summary>Whether a resolved set includes <paramref name="battery"/>, addressed by its flag name.</summary>
    /// <remarks>
    /// The one place that maps flag names onto <see cref="ServerBatteries"/> properties, so the wizard
    /// summary and the tests read the resolved answer rather than re-deriving it from the command line.
    /// </remarks>
    internal static bool Includes(ServerBatteries batteries, string battery) => battery switch
    {
        "pwa" => batteries.Pwa,
        "cqrs" => batteries.Cqrs,
        "data" => batteries.Data,
        "docker" => batteries.Docker,
        "localization" => batteries.Localization,
        "jobs" => batteries.Jobs,
        "mail" => batteries.Mail,
        "cache" => batteries.Cache,
        "outbox" => batteries.Outbox,
        "push" => batteries.Push,
        "snapshots" => batteries.Snapshots,
        "logs" => batteries.Logs,
        "ops" => batteries.Ops,
        "auth" => batteries.Auth,
        _ => false,
    };

    private async Task<int> GenerateDirectAsync(
        TemplateInfo template, string name, string? output, bool dryRun, bool force, bool noRestore, bool noGit,
        ServerBatteries batteries, Func<string, string, ScaffoldResult> build, CancellationToken cancellationToken)
    {
        // rask new MyApp → ./MyApp/ ; --output overrides the destination directory.
        var targetDirectory = Scaffold.TargetDirectory(_workingDirectory, output, name);

        // build() is pure (in-memory strings), so it's safe to run before the existence check — we need its
        // RestoreTarget to know what to guard/restore. Single-project templates restore {name}.csproj at the
        // root; a multi-project template — a front-end one, whose client sits beside an ASP.NET host — has
        // no root csproj and restores its {name}.slnx instead.
        var version = ResolvePackageVersion(CliMetadata.Version);
        var result = build(targetDirectory, version);

        // --dry-run previews the plan without touching disk or restoring.
        if (dryRun)
        {
            WriteHeading($"Would create {template.DisplayName} '{name}':");
            foreach (var external in result.ExternalScaffolds)
            {
                WriteDryRun("run", external.Command + " " + string.Join(" ", external.Arguments));
            }

            foreach (var file in result.Files)
            {
                WriteDryRun("write", Path.GetRelativePath(_workingDirectory, file.Path));
            }

            foreach (var patch in result.Patches)
            {
                WriteDryRun("patch", Path.GetRelativePath(_workingDirectory, patch.Path) + " — " + patch.Description);
            }

            return 0;
        }

        var restoreTarget = result.RestoreTarget is { } relative
            ? Path.Combine(targetDirectory, relative)
            : Path.Combine(targetDirectory, name + ".csproj");
        // The guard used to check only for the restore target, so scaffolding over a directory that already
        // held a Program.cs, a Features/ tree or a wwwroot silently overwrote them — with no --force to
        // consent to it and nothing to undo it. Any existing file is now enough to stop.
        if (!force)
        {
            if (_fileSystem.FileExists(restoreTarget))
            {
                var existing = Path.GetFileName(restoreTarget);
                Console.WriteErrorLine(
                    $"A project already exists at '{targetDirectory}' ({existing}). Choose another name, --output, or pass --force.",
                    ConsoleStyle.Error);
                return 1;
            }

            var clashes = result.Files.Where(f => _fileSystem.FileExists(f.Path)).ToArray();
            if (clashes.Length > 0)
            {
                Console.WriteErrorLine($"'{targetDirectory}' already contains files this would overwrite:", ConsoleStyle.Error);
                foreach (var clash in clashes.Take(5))
                {
                    Console.Error.WriteLine($"  {Path.GetRelativePath(_workingDirectory, clash.Path)}");
                }

                if (clashes.Length > 5)
                {
                    Console.Error.WriteLine($"  …and {(clashes.Length - 5).ToString(CultureInfo.InvariantCulture)} more");
                }

                Console.WriteErrorLine("Choose another name or --output, or pass --force to overwrite.", ConsoleStyle.Error);
                return 1;
            }
        }

        WriteHeading($"Creating {template.DisplayName} '{name}'…");

        // Before our own files, because ours are an overlay on top of what these produce — a vite.config
        // the scaffolder just wrote, an App component we replace. Anything they leave behind stays.
        foreach (var external in result.ExternalScaffolds)
        {
            if (await RunExternalScaffoldAsync(external, targetDirectory, cancellationToken).ConfigureAwait(false) is
                { } failure)
            {
                return failure;
            }
        }

        foreach (var file in result.Files)
        {
            var directory = Path.GetDirectoryName(file.Path);
            if (!string.IsNullOrEmpty(directory))
            {
                _fileSystem.CreateDirectory(directory);
            }

            _fileSystem.WriteAllText(file.Path, file.Content);
            WriteCreated(Path.GetRelativePath(_workingDirectory, file.Path));
        }

        foreach (var patch in result.Patches)
        {
            ApplyPatch(patch);
        }

        // Package refs are already baked into the csproj(s) at the pinned version; restore pulls them so the
        // project builds immediately. The files on disk are complete and correct either way — but a failed
        // restore leaves a project that won't build, so it is reported as a failure rather than a warning
        // that `rask new && dotnet build` would step straight past. --no-restore skips it deliberately.
        var restoreFailed = false;
        if (noRestore)
        {
            Console.WriteLine("Skipped restore (--no-restore) — run 'dotnet restore' before building.", ConsoleStyle.Dim);
        }
        else
        {
            Console.WriteLine("Restoring packages…", ConsoleStyle.Dim);
            restoreFailed = await _process.RunAsync("dotnet", ["restore", restoreTarget], targetDirectory, cancellationToken).ConfigureAwait(false) != 0;
        }

        // The database-backed batteries keep their state in tables that only exist once a migration has been
        // applied, and their processors are hosted services — a faulted BackgroundService stops the host, so
        // an unmigrated app doesn't warn, it exits. That was an opt-in edge case while --data was opt-in;
        // now that the batteries are on by default it would be the first `dotnet run` of every new project.
        // So the first migration is part of scaffolding rather than a step in the next-steps text.
        var migrated = !batteries.Data || noRestore || restoreFailed
            ? (bool?)null
            : await CreateFirstMigrationAsync(targetDirectory, PickEfProject(result, name), cancellationToken)
                .ConfigureAwait(false);

        // After the migration, so Migrations/ is in the initial commit rather than showing up as the first
        // uncommitted change in a project the user hasn't touched yet.
        await InitializeGitAsync(targetDirectory, noGit, cancellationToken).ConfigureAwait(false);

        if (!string.IsNullOrEmpty(result.Notes))
        {
            Console.Out.WriteLine();
            Console.Out.WriteLine(result.Notes);
        }

        if (restoreFailed)
        {
            Console.Out.WriteLine();
            Console.WriteErrorLine(
                $"The project was written to '{targetDirectory}', but restoring its packages failed — it won't build until that succeeds.",
                ConsoleStyle.Error);
            Console.WriteErrorLine("Run 'dotnet restore' there once you're online, or re-run with --no-restore to skip this step.", ConsoleStyle.Error);
            return 1;
        }

        // A warning rather than a failure, and deliberately: the files on disk are complete and correct,
        // and this step is the only one that can need a network for something other than packages (the
        // dotnet-ef tool install). Failing the command would make an offline `rask new` look like it
        // scaffolded nothing.
        if (batteries.Data && migrated != true)
        {
            Console.Out.WriteLine();
            WriteFirstMigrationInstructions(migrated is null);
        }

        return 0;
    }

    /// <summary>
    /// The project that owns the <c>DbContext</c>: the <c>.Server</c> half of a multi-project template,
    /// the single project otherwise.
    /// </summary>
    /// <remarks>
    /// Read off the scaffold rather than rebuilt from the template key. <c>ProjectLocator</c> can't be used
    /// here: it walks <em>up</em> from the working directory, so on a template whose root holds only a
    /// <c>.slnx</c> it would climb out of the new project and migrate whatever it found above it.
    /// </remarks>
    private static string? PickEfProject(ScaffoldResult result, string name)
    {
        var projects = result.Files
            .Select(file => file.Path)
            .Where(path => path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        return projects.FirstOrDefault(path =>
                   Path.GetFileName(path).Equals(name + ".Server.csproj", StringComparison.OrdinalIgnoreCase))
               ?? projects.FirstOrDefault(path =>
                   Path.GetFileName(path).Equals(name + ".csproj", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Create and apply the project's first migration, so its first run has the tables the batteries need.
    /// </summary>
    /// <remarks>
    /// Delegated to <see cref="DbCommand"/> rather than reimplemented against <c>dotnet ef</c>: it already
    /// installs the EF tools on first use, adds the design package the tools require, and builds the
    /// argument list. Running the same code the user would run next means the project ends up in exactly
    /// the state <c>rask db add Init &amp;&amp; rask db update</c> leaves it in.
    ///
    /// <para>
    /// <c>--project</c> is passed explicitly for the reason given on <see cref="PickEfProject"/>.
    /// </para>
    /// </remarks>
    private async Task<bool> CreateFirstMigrationAsync(
        string targetDirectory, string? efProject, CancellationToken cancellationToken)
    {
        if (efProject is null)
        {
            return false;
        }

        var db = new DbCommand(Console, _fileSystem, _process, targetDirectory);

        Console.WriteLine("Creating the first migration…", ConsoleStyle.Dim);
        if (await db.ExecuteAsync(["add", "Init", "--project", efProject], cancellationToken).ConfigureAwait(false) != 0)
        {
            return false;
        }

        Console.WriteLine("Applying it to the database…", ConsoleStyle.Dim);
        return await db.ExecuteAsync(["update", "--project", efProject], cancellationToken).ConfigureAwait(false) == 0;
    }

    /// <summary>What to run when the first migration was skipped or didn't succeed.</summary>
    private void WriteFirstMigrationInstructions(bool skipped)
    {
        Console.WriteLine(
            skipped
                ? "The first migration was skipped along with the restore. Before the first run:"
                : "The first migration didn't complete. Before the first run:",
            ConsoleStyle.Dim);
        Console.Out.WriteLine("  rask db add Init");
        Console.Out.WriteLine("  rask db update");
        Console.WriteLine(
            "The background pillars store their state in your database, and a hosted service that can't "
            + "find its table stops the app — so this has to happen before `dotnet run`.",
            ConsoleStyle.Dim);
    }

    /// <summary>
    ///     Runs a framework's own scaffolder, or null when it succeeded.
    /// </summary>
    /// <remarks>
    ///     The missing-command case is checked before the run rather than after: an executable that is not
    ///     there surfaces as a Win32Exception with a message naming a file, which reads as a bug in the tool
    ///     rather than as "install Node.js".
    /// </remarks>
    private async Task<int?> RunExternalScaffoldAsync(
        ExternalScaffold external, string targetDirectory, CancellationToken cancellationToken)
    {
        Console.WriteLine(external.Description, ConsoleStyle.Dim);

        // The scaffolder writes INTO the target directory, which nothing has created yet on a fresh run.
        _fileSystem.CreateDirectory(targetDirectory);

        int exitCode;
        try
        {
            exitCode = await _process
                .RunAsync(external.Command, external.Arguments, targetDirectory, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            Console.WriteErrorLine(
                $"'{external.Command}' is not available, and this template needs it. {external.MissingHint}",
                ConsoleStyle.Error);
            return 1;
        }

        if (exitCode != 0)
        {
            Console.WriteErrorLine(
                $"'{external.Command} {string.Join(" ", external.Arguments)}' failed (exit {exitCode.ToString(CultureInfo.InvariantCulture)}). "
                + "Nothing further was written.",
                ConsoleStyle.Error);
            return 1;
        }

        return null;
    }

    /// <summary>
    ///     Applies an edit to a file the scaffold did not write, reporting rather than failing when it is
    ///     not there.
    /// </summary>
    /// <remarks>
    ///     Not fatal on purpose. These amend an external scaffolder's output, and that output is not ours to
    ///     depend on the shape of — a create-vite that stops writing a .gitignore should cost a line of
    ///     advice, not a failed scaffold with a half-written project on disk.
    /// </remarks>
    private void ApplyPatch(ScaffoldPatch patch)
    {
        var relative = Path.GetRelativePath(_workingDirectory, patch.Path);
        if (!_fileSystem.FileExists(patch.Path))
        {
            Console.WriteLine($"Skipped {relative} ({patch.Description}) — it isn't there.", ConsoleStyle.Dim);
            return;
        }

        try
        {
            _fileSystem.WriteAllText(patch.Path, patch.Transform(_fileSystem.ReadAllText(patch.Path)));
            WriteCreated($"{relative} ({patch.Description})");
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Text.Json.JsonException)
        {
            Console.WriteLine($"Could not patch {relative} ({patch.Description}) — {ex.Message}", ConsoleStyle.Dim);
        }
    }

    /// <summary>
    /// Why <paramref name="output"/> can't be used, or null when it can.
    /// </summary>
    /// <remarks>
    /// Both failures used to surface late and badly. An empty (or whitespace) value resolved to the current
    /// directory, so the project was written <em>into wherever you were standing</em> instead of a folder of
    /// its own — no error, no clue. A value naming an existing file got as far as printing "Creating …"
    /// before the first write threw, and the resulting "The file '…' already exists." named the path without
    /// saying what the command had wanted with it.
    /// </remarks>
    private string? ValidateOutput(string? output)
    {
        if (output is null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(output))
        {
            return "--output needs a directory. Leave it off to scaffold into ./<name>.";
        }

        var resolved = Path.GetFullPath(Path.Combine(_workingDirectory, output));
        return _fileSystem.FileExists(resolved)
            ? $"--output '{output}' is a file. Point it at a directory (it's created if missing)."
            : null;
    }

    /// <summary>
    /// Put the new project under version control: <c>git init</c>, stage everything, and make one commit, so
    /// the very first thing the user changes is already a diff against a known-good starting point.
    /// <para>
    /// Every part of this is best-effort. Git may not be installed, may have no <c>user.email</c> configured,
    /// or the directory may already be inside a repository — none of which is a reason to fail a scaffold that
    /// otherwise succeeded, so a failure downgrades to a dim note. <c>--no-git</c> skips it outright.
    /// </para>
    /// </summary>
    private async Task InitializeGitAsync(string targetDirectory, bool noGit, CancellationToken cancellationToken)
    {
        if (noGit)
        {
            return;
        }

        // Already inside a repository (a monorepo, or `rask new` into an existing checkout): the files belong
        // to that history. Initialising a nested repo there would quietly detach them from it.
        if (await IsInsideRepositoryAsync(targetDirectory, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        // The commit identity is supplied per-command rather than written to the repo's config: a machine with
        // no global user.email would otherwise fail the commit, and one with an identity keeps using its own
        // from the second commit onwards.
        var steps = new[]
        {
            new[] { "init", "--quiet" },
            ["add", "--all"],
            ["-c", "user.name=rask", "-c", "user.email=rask@localhost", "commit", "--quiet", "-m", "Initial commit from rask new"],
        };

        foreach (var step in steps)
        {
            if (await _process.RunAsync("git", step, targetDirectory, cancellationToken).ConfigureAwait(false) != 0)
            {
                Console.WriteLine("Skipped git setup — initialize the repository yourself with 'git init'.", ConsoleStyle.Dim);
                return;
            }
        }

        Console.WriteLine("Initialized a git repository with one commit.", ConsoleStyle.Dim);
    }

    /// <summary>True when <paramref name="directory"/> already sits inside a git working tree.</summary>
    private async Task<bool> IsInsideRepositoryAsync(string directory, CancellationToken cancellationToken)
    {
        var result = await _process
            .CaptureAsync("git", ["rev-parse", "--is-inside-work-tree"], directory, cancellationToken)
            .ConfigureAwait(false);

        return result.ExitCode == 0 && result.StandardOutput.Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Resolve the package version to pin in a generated project: the CLI's own version when it's a published
    /// stable, else the latest-stable fallback (a dev/CI prerelease isn't on NuGet). Pure — unit-tested directly.
    /// </summary>
    /// <summary>
    /// The version to pin generated <c>PackageReference</c>s at.
    ///
    /// <para>A released CLI stamps a stable version and pins itself — the CLI and the packages ship under
    /// one tag, so a project is pinned to the CLI that made it. A dev or CI build stamps a MinVer
    /// prerelease (<c>0.19.1-alpha.0.5+sha</c>) that was never published, and pinning that would produce a
    /// project which cannot restore.</para>
    ///
    /// <para>So a prerelease is walked back to the release it came after. MinVer names a prerelease for the
    /// version it is <em>heading towards</em>, bumping the patch: the build after <c>v0.19.0</c> is
    /// <c>0.19.1-alpha.N</c>, whose last published predecessor is <c>0.19.0</c>. This used to be a
    /// hardcoded constant, which silently rotted two minor versions behind the repo.</para>
    /// </summary>
    internal static string ResolvePackageVersion(string cliVersion)
    {
        if (string.IsNullOrEmpty(cliVersion) || cliVersion == "0.0.0")
        {
            return cliVersion;
        }

        var dash = cliVersion.IndexOf('-', StringComparison.Ordinal);
        if (dash < 0)
        {
            return cliVersion; // a released build: pin to itself
        }

        var parts = cliVersion[..dash].Split('.');
        if (parts.Length != 3
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var minor)
            || !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var patch))
        {
            return cliVersion;
        }

        // 0.19.1-alpha.N came after v0.19.0 (MinVer's default patch bump).
        if (patch > 0)
        {
            return $"{parts[0]}.{parts[1]}.{(patch - 1).ToString(CultureInfo.InvariantCulture)}";
        }

        // 0.18.0-alpha.N came after a v0.17.x — which patch, we can't know, but .0 was certainly published
        // and restores fine. Pinning slightly behind the newest release beats not restoring at all.
        if (minor > 0)
        {
            return $"{parts[0]}.{(minor - 1).ToString(CultureInfo.InvariantCulture)}.0";
        }

        // 1.0.0-alpha.N — nothing published under this major to walk back to. Pinning a guess would be
        // worse than pinning the prerelease, which at least fails loudly and legibly at restore.
        return cliVersion;
    }
}
