using System.Globalization;
using Rask.Cli.Scaffolding;
using Rask.Cli.Templates;
using Spectre.Console;

namespace Rask.Cli.Commands;

/// <summary>
/// <c>rask new</c> — scaffold a Rask project. The CLI is the scaffolding authority: every template
/// (<c>server</c>, <c>wasm</c>, <c>wasm-hosted</c>, <c>native</c>) is generated directly — files written +
/// package refs baked at the CLI's own version + <c>dotnet restore</c> — with no <c>dotnet new</c> /
/// Rask.Templates dependency.
/// </summary>
internal sealed class NewCommand(IConsole console, IFileSystem fileSystem, IProcessRunner process, string workingDirectory)
    : CliCommand(console)
{
    private readonly IFileSystem _fileSystem = fileSystem;
    private readonly IProcessRunner _process = process;
    private readonly string _workingDirectory = workingDirectory;

    /// <summary>
    /// Where a native app's UI comes from (<c>--host</c>). <c>local</c> runs the components on the device;
    /// the other two point a native shell at a Rask app you host. Both remote modes scaffold the same
    /// shell — the WebView is handed a trusted origin and does not care what serves it — and differ only
    /// in the guidance they carry, because pointing at a live server and pointing at a published WASM
    /// bundle are different enough operationally to be worth saying out loud.
    /// </summary>
    private static readonly string[] NativeHosts = ["local", "server", "wasm-hosted"];

    /// <summary>
    /// The platforms the native template can target (<c>--platform</c>, repeatable). Omitting it targets
    /// both — the common case, and the one where leaving a platform out later costs nothing.
    /// </summary>
    private static readonly string[] NativePlatforms = ["ios", "android"];

    /// <summary>The opt-in feature flags <c>rask new</c> forwards to a template (as <c>--flag</c>).</summary>
    internal static readonly string[] FeatureFlags =
    [
        "auth", "pwa", "cqrs", "data", "docker",
        "jobs", "mail", "cache", "outbox", "push", "snapshots", "logs", "ops", "all-batteries",
    ];


    public override string Name => "new";

    public override string Summary => "Create a new Rask project from a template.";

    // The shape only — the flags are listed once, in the schema below, which --help renders directly.
    public override string Usage => "rask new <name> [options]";

    public override IReadOnlyList<(string Name, string Description)> Arguments =>
        [("<name>", "Name of the project to create (scaffolds ./<name>/).")];

    public override IReadOnlyList<string> Examples =>
    [
        "rask new Shop",
        "rask new Shop --template wasm --pwa",
        "rask new Api --template server --auth --docker",
        "rask new Blog --data --docker",
        "rask new Shop --all-batteries --auth --docker",
        "rask new MyApp --template native --host server",
    ];

    public override ArgumentSchema? OptionSchema => CreateSchema();

    /// <summary>The flag/option schema — shared by <see cref="ExecuteAsync"/> and <c>--help</c> so they can't drift.</summary>
    private static ArgumentSchema CreateSchema() =>
        new ArgumentSchema()
            .Option("template", 't', "name", "Template to scaffold (default: server).", choices: TemplateCatalog.Keys)
            .Option("output", 'o', "dir", "Directory to create the project in (default: ./<name>).")
            .Option("name", 'n', "name", "Project name, if not given positionally.")
            .Option("host", valueHint: "mode", description: "Native template only: where the UI comes from — 'local' runs the components on the device (default), 'server' or 'wasm-hosted' make the app a thin native shell over a Rask app you host.", choices: NativeHosts)
            .MultiOption("platform", valueHint: "name", description: "Native template only: a platform to target, repeatable (default: both). Only the chosen platforms get a TFM, a manifest and a head.", choices: NativePlatforms)
            .Flag("auth", description: "Add cookie authentication (login + members pages).")
            .Flag("pwa", description: "Add a PWA manifest, icon, and offline page.")
            .Flag("cqrs", description: "Wire up Rask.Cqrs. On 'wasm-hosted' this also makes the client dispatch to the server — no HttpClient, no endpoints to write.")
            .Flag("data", description: "Pre-wire a database + EF Core: an AppDbContext your features map through (server only).")
            .Flag("docker", description: "Add a Dockerfile and .dockerignore for container deploys.")
            .Flag("no-restore", description: "Don't run dotnet restore after scaffolding (for offline use).")
            .Flag("no-git", description: "Don't initialize a git repository (one is created with an initial commit by default).")
            .Flag("no-bootstrap", description: "Render pages with plain elements and a small built-in stylesheet instead of Rask.Bootstrap.")
            .Flag("force", description: "Scaffold into a directory that already has files in it, overwriting on collision.")
            .Flag("jobs", description: "Durable background jobs on the app's own database (implies --data).")
            .Flag("mail", description: "Transactional email queued on the app's own database (implies --data).")
            .Flag("cache", description: "A database-backed ICache + IDistributedCache (implies --data).")
            .Flag("outbox", description: "Transactional outbox for durable domain events (implies --data).")
            .Flag("push", description: "Server-sent Web Push with subscribe endpoints (implies --pwa).")
            .Flag("snapshots", description: "Scheduled point-in-time SQLite backups (implies --data).")
            .Flag("logs", description: "Keep the application log in a database of its own, so it survives a restart.")
            .Flag("ops", description: "An operator dashboard at /_rask over every battery's table (implies --data).")
            .Flag("all-batteries", description: "Every battery above — the full One Person Framework stack.")
            .Flag("dry-run", description: "Print the files that would be written without touching disk.");

    public override Task<int> ExecuteAsync(IReadOnlyList<string> args, CancellationToken cancellationToken) =>
        ExecuteAsync(args, allowWizard: true, cancellationToken);

    private async Task<int> ExecuteAsync(IReadOnlyList<string> args, bool allowWizard, CancellationToken cancellationToken)
    {
        var schema = CreateSchema();

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

        var requestedFlags = FeatureFlags.Where(parsed.HasFlag).ToArray();
        var unsupported = requestedFlags.Where(flag => !template.SupportedFlags.Contains(flag)).ToArray();
        if (unsupported.Length > 0)
        {
            var supported = template.SupportedFlags.Count == 0
                ? "(none)"
                : string.Join(", ", template.SupportedFlags.OrderBy(f => f, StringComparer.Ordinal).Select(f => "--" + f));
            var rejected = string.Join(", ", unsupported.Select(f => "--" + f));
            return Fail($"Template '{template.Key}' does not support: {rejected}. Supported flags: {supported}.");
        }

        // --host only applies to the native template (which mode to scaffold). Reject it elsewhere so a
        // misplaced flag is a clear error rather than silently ignored.
        var host = parsed.Option("host");
        if (host is not null && template.Key != "native")
        {
            return Fail($"Template '{template.Key}' does not support --host. It applies only to the native template (--host local|server|wasm-hosted).");
        }

        var platforms = parsed.MultiOption("platform");
        if (platforms.Count > 0 && template.Key != "native")
        {
            return Fail($"Template '{template.Key}' does not support --platform. It applies only to the native template (--platform ios|android).");
        }

        // Native is generated directly, but with its own shape: a --host choice, the platforms to target, a
        // single package, and no feature flags. The values are declared choices, so they are already valid.
        if (template.Key == "native")
        {
            host ??= "local";

            // Naming neither is the default (both); naming one narrows to it. The parse normalises the
            // spelling, so an ordinal comparison is enough here.
            var ios = platforms.Count == 0 || platforms.Contains("ios", StringComparer.Ordinal);
            var android = platforms.Count == 0 || platforms.Contains("android", StringComparer.Ordinal);

            return await GenerateDirectAsync(
                template, name, parsed.Option("output"), parsed.HasFlag("dry-run"), parsed.HasFlag("force"),
                parsed.HasFlag("no-restore"), parsed.HasFlag("no-git"),
                (dir, version) => ProjectGenerator.GenerateNative(dir, name, host, version, ios, android),
                cancellationToken).ConfigureAwait(false);
        }

        // Every web template is generated directly by the CLI (server, wasm, wasm-hosted). native is handled
        // above with its own shape; the key here is one of those three (validated by TemplateCatalog.TryGet).
        return await GenerateDirectAsync(
            template, name, parsed.Option("output"), parsed.HasFlag("dry-run"), parsed.HasFlag("force"),
            parsed.HasFlag("no-restore"), parsed.HasFlag("no-git"),
            (dir, version) =>
            {
                bool auth = requestedFlags.Contains("auth"), pwa = requestedFlags.Contains("pwa"),
                    docker = requestedFlags.Contains("docker");

                // Bootstrap is the default; --no-bootstrap opts the generated pages out of the component
                // library and onto the shell's own baseline stylesheet.
                var bootstrap = !parsed.HasFlag("no-bootstrap");
                return template.Key switch
                {
                    "wasm" => ProjectGenerator.GenerateWasm(dir, name, auth, pwa, docker, version, bootstrap),
                    // Push is cleared rather than rejected: an explicit --push on this template already
                    // fails fast against TemplateCatalog, so the only way to arrive here with it set is
                    // --all-batteries, and "every battery this template has" is the honest reading of that.
                    "wasm-hosted" => ProjectGenerator.GenerateWasmHosted(
                        dir, name, ToBatteries(requestedFlags, bootstrap) with { Push = false }, version),
                    _ => ProjectGenerator.GenerateServer(dir, name, ToBatteries(requestedFlags, bootstrap), version),
                };
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Maps the requested flag names onto the server template's battery set. <c>--all-batteries</c> expands to
    /// every DB-backed pillar, which is what the tutorial and the showcase sample use.
    /// </summary>
    internal static ServerBatteries ToBatteries(
        IReadOnlyCollection<string> flags,
        bool bootstrap = true)
    {
        var all = flags.Contains("all-batteries");
        return new ServerBatteries
        {
            Bootstrap = bootstrap,
            Auth = flags.Contains("auth"),
            Pwa = flags.Contains("pwa"),
            Cqrs = flags.Contains("cqrs"),
            Data = flags.Contains("data"),
            Docker = flags.Contains("docker"),
            Jobs = all || flags.Contains("jobs"),
            Mail = all || flags.Contains("mail"),
            Cache = all || flags.Contains("cache"),
            Outbox = all || flags.Contains("outbox"),
            Push = all || flags.Contains("push"),
            Snapshots = all || flags.Contains("snapshots"),
            Logs = all || flags.Contains("logs"),
            Ops = all || flags.Contains("ops"),
        };
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

        if (templateKey == "native")
        {
            if (parsed.Option("host") is null)
            {
                filled.Add("--host");
                filled.Add(prompt.Select(
                    "Where the app's UI comes from",
                    [
                        ("local", "[bold]The device[/] [dim]— components run in the app itself, works offline[/]"),
                        ("server", "[bold]A Rask server[/] [dim]— a thin native shell over a live server you run[/]"),
                        ("wasm-hosted", "[bold]A wasm-hosted app[/] [dim]— a thin native shell over a published WASM bundle[/]"),
                    ],
                    "local"));
            }

            if (parsed.MultiOption("platform").Count == 0)
            {
                // A checklist, because both are ordinarily wanted and picking one is the exception. Ticking
                // nothing means "both" rather than "no platforms" — an app targeting neither is not a thing
                // to scaffold, so the empty answer is read as the default instead of as an error to correct.
                var chosen = prompt.MultiSelect(
                    "Platforms [dim](space to tick; none ticked = both)[/]",
                    [
                        ("android", "[bold]Android[/] [dim]— emulator or device, needs the android workload[/]"),
                        ("ios", "[bold]iOS[/] [dim]— simulator or device, needs macOS + Xcode[/]"),
                    ]);

                foreach (var platform in chosen)
                {
                    filled.Add("--platform");
                    filled.Add(platform);
                }
            }

            WriteWizardSummary(filled, template);
            return filled;
        }

        // Styling. Asked as a choice rather than a "skip Bootstrap?" confirm, because both answers are a
        // real starting point and neither is a subtraction from the other.
        if (!parsed.HasFlag("no-bootstrap"))
        {
            var styling = prompt.Select(
                "Styling",
                [
                    ("bootstrap", "[bold]Rask.Bootstrap[/] [dim]— Bs* components over Bootstrap 5.3, no CDN[/]"),
                    ("plain", "[bold]Plain elements[/] [dim]— a small stylesheet in the app shell, no CSS framework[/]"),
                ],
                "bootstrap");

            if (styling == "plain")
            {
                filled.Add("--no-bootstrap");
            }
        }

        // Docker is asked on its own rather than from the battery list: it is about how the app ships, not
        // about what the app does, and it's the one every template supports.
        if (!parsed.HasFlag("docker") && template.SupportedFlags.Contains("docker")
            && prompt.Confirm("Add a [bold]Dockerfile[/] for container deploys?", @default: false))
        {
            filled.Add("--docker");
        }

        // Three flags are held back from the list, each for its own reason. `snapshots` is asked on its
        // own below, after the battery list, because it is a follow-up to having a database rather than a
        // battery in its own right. `docker` was just asked above. And `all-batteries` is what a checklist
        // already does — offering "tick everything" as one of the things to tick invites ticking it *and*
        // its members, which is the same app described twice.
        var offered = FeatureFlags
            .Where(f => f is not ("snapshots" or "docker" or "all-batteries"))
            .Where(template.SupportedFlags.Contains)
            .ToArray();

        // A command line that already named any battery has answered this question — don't re-ask it.
        var batteriesGiven = FeatureFlags.Any(parsed.HasFlag);
        if (!batteriesGiven && offered.Length > 0)
        {
            var descriptions = CreateSchema().Declared.ToDictionary(o => o.LongName, o => o.Description, StringComparer.Ordinal);
            var chosen = prompt.MultiSelect(
                "Batteries [dim](optional)[/]",
                [.. offered.Select(f => (f, $"[bold]--{f}[/] [dim]— {descriptions.GetValueOrDefault(f)}[/]"))]);

            filled.AddRange(chosen.Select(flag => "--" + flag));
        }

        if (!batteriesGiven
            && template.SupportedFlags.Contains("snapshots")
            && filled.Any(DataImplyingFlags.Contains)
            && !filled.Contains("--all-batteries", StringComparer.Ordinal)
            && prompt.Confirm("Back SQLite up on a schedule ([bold]--snapshots[/])?", @default: false))
        {
            filled.Add("--snapshots");
        }

        WriteWizardSummary(filled, template);
        return filled;
    }

    /// <summary>
    /// Restate the answers before the files start appearing, so the scaffolding output is read as the
    /// result of a decision rather than as a wall of paths.
    /// </summary>
    /// <remarks>
    /// Only rows that were actually decided are shown. A summary listing every axis would tell a native
    /// app it had chosen Rask.Bootstrap and declined Docker — two questions that template never asks and
    /// does not support — which is worse than saying nothing, because it reads as confirmation.
    /// </remarks>
    private void WriteWizardSummary(IReadOnlyList<string> args, TemplateInfo template)
    {
        var batteries = args.Where(a => a.StartsWith("--", StringComparison.Ordinal))
            .Select(a => a[2..])
            .Where(FeatureFlags.Contains)
            .ToArray();

        var grid = new Grid();
        grid.AddColumn(new GridColumn().NoWrap().PadRight(2));
        grid.AddColumn();
        grid.AddRow(Label("📦", "Project"), new Text(args[0]));
        grid.AddRow(Label("🧩", "Type"), new Text(template.DisplayName));

        if (template.Key == "native")
        {
            var host = args
                .SkipWhile(a => !a.Equals("--host", StringComparison.Ordinal))
                .Skip(1)
                .FirstOrDefault() ?? "local";

            grid.AddRow(Label("📱", "UI from"), new Text(NativeHostSummary(host)));

            var picked = args
                .Select((a, i) => (a, i))
                .Where(x => x.a.Equals("--platform", StringComparison.Ordinal) && x.i + 1 < args.Count)
                .Select(x => args[x.i + 1])
                .ToArray();

            grid.AddRow(
                Label("🤖", "Platforms"),
                new Text(picked.Length == 0 ? "Android, iOS" : string.Join(", ", picked.Select(NativePlatformSummary))));
        }
        else
        {
            grid.AddRow(
                Label("🎨", "Styling"),
                new Text(args.Contains("--no-bootstrap", StringComparer.Ordinal) ? "plain elements" : "Rask.Bootstrap"));

            if (batteries.Any(DataImplyingFlags.Select(f => f[2..]).Contains))
            {
                grid.AddRow(Label("🗄️", "Database"), new Text("SQLite (one file, no server)"));
            }

            grid.AddRow(
                Label("🐳", "Docker"),
                new Text(batteries.Contains("docker") ? "yes" : "no"));

            grid.AddRow(
                Label("🔋", "Batteries"),
                new Text(batteries.Where(b => b != "docker").ToArray() is { Length: > 0 } rest
                    ? string.Join(", ", rest)
                    : "none"));
        }

        var ansi = Console.Ansi;
        ansi.WriteLine();
        ansi.Write(new RaggedRight(new Padder(grid, new Padding(1, 0, 0, 1))));

        static string NativePlatformSummary(string platform) =>
            platform.Equals("ios", StringComparison.Ordinal) ? "iOS" : "Android";

        static string NativeHostSummary(string host) => host switch
        {
            "server" => "a Rask server you host",
            "wasm-hosted" => "a wasm-hosted app you host",
            _ => "the device (works offline)",
        };

        Text Label(string emoji, string text) =>
            new(Branding.Label(Console, emoji, text), ConsoleStyling.Of(ConsoleStyle.Dim));
    }

    /// <summary>
    /// The flags that pull in a database, so the wizard knows when the snapshots question is worth asking
    /// and when the summary has a database to report. Mirrors the implications
    /// <see cref="ServerBatteries.Normalized"/> applies.
    /// </summary>
    private static readonly string[] DataImplyingFlags =
        ["--data", "--jobs", "--mail", "--cache", "--outbox", "--ops", "--all-batteries"];

    private async Task<int> GenerateDirectAsync(
        TemplateInfo template, string name, string? output, bool dryRun, bool force, bool noRestore, bool noGit,
        Func<string, string, ScaffoldResult> build, CancellationToken cancellationToken)
    {
        // rask new MyApp → ./MyApp/ ; --output overrides the destination directory.
        var targetDirectory = Scaffold.TargetDirectory(_workingDirectory, output, name);

        // build() is pure (in-memory strings), so it's safe to run before the existence check — we need its
        // RestoreTarget to know what to guard/restore. Single-project templates restore {name}.csproj at the
        // root; a multi-project template (wasm-hosted) has no root csproj and restores its {name}.sln instead.
        var version = ResolvePackageVersion(CliMetadata.Version);
        var result = build(targetDirectory, version);

        // --dry-run previews the plan without touching disk or restoring.
        if (dryRun)
        {
            WriteHeading($"Would create {template.DisplayName} '{name}':");
            foreach (var file in result.Files)
            {
                WriteDryRun("write", Path.GetRelativePath(_workingDirectory, file.Path));
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

        return 0;
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
