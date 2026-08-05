using System.Globalization;
using Rask.Cli.Scaffolding;
using Rask.Cli.Templates;

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

    /// <summary>The opt-in feature flags <c>rask new</c> forwards to a template (as <c>--flag</c>).</summary>
    internal static readonly string[] FeatureFlags =
    [
        "auth", "pwa", "cqrs", "data", "docker",
        "jobs", "mail", "cache", "outbox", "push", "snapshots", "logs", "ops", "all-batteries",
    ];


    public override string Name => "new";

    public override string Summary => "Create a new Rask project from a template.";

    public override string Usage =>
        "rask new <name> [--template server|wasm|wasm-hosted|native] [--auth] [--pwa] [--cqrs] [--data] "
        + "[--jobs] [--mail] [--cache] [--outbox] [--push] [--snapshots] [--logs] [--ops] "
        + "[--all-batteries] [--docker] [--host local|server] [--output <dir>]";

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
            .Option("template", 't', "name", "Template to scaffold: server (default), wasm, wasm-hosted, or native.")
            .Option("output", 'o', "dir", "Directory to create the project in (default: ./<name>).")
            .Option("name", 'n', "name", "Project name, if not given positionally.")
            .Option("host", valueHint: "local|server", description: "Native host mode: local (default) or server. Native template only.")
            .Option("database", valueHint: DatabaseCatalog.Keys, description: "Database for --data: sqlite (default, one file, no server) or postgres. Server template only.")
            .Flag("auth", description: "Add cookie authentication (login + members pages).")
            .Flag("pwa", description: "Add a PWA manifest, icon, and offline page.")
            .Flag("cqrs", description: "Wire up Rask.Cqrs (server template only).")
            .Flag("data", description: "Pre-wire a database + EF Core: an AppDbContext ready for `rask generate feature --context AppDbContext` (server only). See --database.")
            .Flag("docker", description: "Add a Dockerfile and .dockerignore for container deploys.")
            .Flag("no-restore", description: "Don't run dotnet restore after scaffolding (for offline use).")
            .Flag("force", description: "Scaffold into a directory that already has files in it, overwriting on collision.")
            .Flag("jobs", description: "Durable background jobs on the app's own database (implies --data).")
            .Flag("mail", description: "Transactional email queued on the app's own database (implies --data).")
            .Flag("cache", description: "A database-backed ICache + IDistributedCache (implies --data).")
            .Flag("outbox", description: "Transactional outbox for durable domain events (implies --data).")
            .Flag("push", description: "Server-sent Web Push with subscribe endpoints (implies --pwa).")
            .Flag("snapshots", description: "Scheduled point-in-time SQLite backups (implies --data).")
            .Flag("logs", description: "Keep the application log in a database of its own, so it survives a restart.")
            .Flag("ops", description: "An operator dashboard at /_ops over every battery's table (implies --data).")
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

        var name = parsed.Option("name") ?? parsed.Positionals.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(name))
        {
            // No name given. On a terminal, walk an interactive wizard and re-run with the answers
            // (allowWizard:false bounds this to one hop); piped/scripted, keep the hard-error contract.
            var prompt = new Prompt(Console);
            if (allowWizard && prompt.Interactive)
            {
                return await ExecuteAsync(RunWizard(prompt), allowWizard: false, cancellationToken).ConfigureAwait(false);
            }

            Console.Error.WriteLine("A project name is required.");
            Console.Error.WriteLine($"Usage: {Usage}");
            return 1;
        }

        // The name becomes the root namespace and the csproj filename, so an invalid one (a dash, a leading
        // digit, a keyword) would scaffold a project that never compiles. Reject it up front with guidance
        // rather than writing files the user then has to throw away.
        if (!Identifiers.IsValidNamespaceName(name))
        {
            Console.Error.WriteLine(
                $"'{name}' isn't a valid project name — it becomes the root namespace, so each dot-separated part must "
                + "start with a letter or underscore and contain only letters, digits, or underscores (e.g. Shop or Contoso.Shop).");
            return 1;
        }

        var templateKey = parsed.Option("template") ?? TemplateCatalog.Default.Key;
        if (!TemplateCatalog.TryGet(templateKey, out var template))
        {
            var available = string.Join(", ", TemplateCatalog.All.Select(t => t.Key));
            Console.Error.WriteLine($"Unknown template '{templateKey}'. Available templates: {available}.");
            return 1;
        }

        var requestedFlags = FeatureFlags.Where(parsed.HasFlag).ToArray();
        var unsupported = requestedFlags.Where(flag => !template.SupportedFlags.Contains(flag)).ToArray();
        if (unsupported.Length > 0)
        {
            var supported = template.SupportedFlags.Count == 0
                ? "(none)"
                : string.Join(", ", template.SupportedFlags.OrderBy(f => f, StringComparer.Ordinal).Select(f => "--" + f));
            var rejected = string.Join(", ", unsupported.Select(f => "--" + f));
            Console.Error.WriteLine($"Template '{template.Key}' does not support: {rejected}. Supported flags: {supported}.");
            return 1;
        }

        // --database only applies to the server template, which is the only one with a database at all.
        var databaseKey = parsed.Option("database");
        if (databaseKey is not null && template.Key != "server")
        {
            Console.Error.WriteLine(
                $"Template '{template.Key}' does not support --database. It applies only to the server template, "
                + "which is the only one that scaffolds a database.");
            return 1;
        }

        if (!DatabaseCatalog.TryGet(databaseKey ?? DatabaseCatalog.Default.Key, out var database))
        {
            var available = string.Join(", ", DatabaseCatalog.All.Select(d => d.Key));
            Console.Error.WriteLine($"Unknown database '{databaseKey}'. Available databases: {available}.");
            return 1;
        }

        // Snapshots copy the database file, so on a client-server database the battery has no meaning.
        // Reject rather than drop it: a backup that silently didn't get wired is discovered too late.
        if (requestedFlags.Contains("snapshots") && !database.IsFileBased)
        {
            Console.Error.WriteLine(
                $"--snapshots needs a file-based database, but --database {database.Key} is a server. "
                + $"Scheduled snapshots (and Litestream continuous backup) copy the SQLite file; on "
                + $"{database.DisplayName} use your provider's backups instead. Drop --snapshots, or use --database sqlite.");
            return 1;
        }

        // --host only applies to the native template (which mode to scaffold). Reject it elsewhere so a
        // misplaced flag is a clear error rather than silently ignored.
        var host = parsed.Option("host");
        if (host is not null && template.Key != "native")
        {
            Console.Error.WriteLine($"Template '{template.Key}' does not support --host. It applies only to the native template (--host local|server).");
            return 1;
        }

        // Native is generated directly, but with its own shape: a --host choice (local|server), a single
        // package, and no feature flags.
        if (template.Key == "native")
        {
            host ??= "local";
            if (host is not ("local" or "server"))
            {
                Console.Error.WriteLine($"Invalid --host '{host}'. The native template supports: local, server.");
                return 1;
            }

            return await GenerateDirectAsync(
                template, name, parsed.Option("output"), parsed.HasFlag("dry-run"), parsed.HasFlag("force"), parsed.HasFlag("no-restore"),
                (dir, version) => ProjectGenerator.GenerateNative(dir, name, host, version),
                cancellationToken).ConfigureAwait(false);
        }

        // Every web template is generated directly by the CLI (server, wasm, wasm-hosted). native is handled
        // above with its own shape; the key here is one of those three (validated by TemplateCatalog.TryGet).
        return await GenerateDirectAsync(
            template, name, parsed.Option("output"), parsed.HasFlag("dry-run"), parsed.HasFlag("force"), parsed.HasFlag("no-restore"),
            (dir, version) =>
            {
                bool auth = requestedFlags.Contains("auth"), pwa = requestedFlags.Contains("pwa"),
                    docker = requestedFlags.Contains("docker");
                return template.Key switch
                {
                    "wasm" => ProjectGenerator.GenerateWasm(dir, name, auth, pwa, docker, version),
                    "wasm-hosted" => ProjectGenerator.GenerateWasmHosted(dir, name, auth, pwa, docker, version),
                    _ => ProjectGenerator.GenerateServer(dir, name, ToBatteries(requestedFlags, database.Provider), version),
                };
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Maps the requested flag names onto the server template's battery set. <c>--all-batteries</c> expands to
    /// every DB-backed pillar, which is what the tutorial and the showcase sample use.
    /// </summary>
    /// <remarks>
    /// <paramref name="provider"/> narrows the <c>--all-batteries</c> expansion: snapshots copy a database
    /// <em>file</em>, so on a client-server database "every battery" simply doesn't include them. That is not
    /// the same as dropping a battery the user asked for by name — an explicit <c>--snapshots</c> against
    /// such a provider is rejected in <see cref="ExecuteAsync"/> instead, because silently ignoring a
    /// requested backup is the kind of thing you discover after losing data.
    /// </remarks>
    internal static ServerBatteries ToBatteries(
        IReadOnlyCollection<string> flags,
        DatabaseProvider provider = DatabaseProvider.Sqlite)
    {
        var all = flags.Contains("all-batteries");
        return new ServerBatteries
        {
            Auth = flags.Contains("auth"),
            Pwa = flags.Contains("pwa"),
            Cqrs = flags.Contains("cqrs"),
            Data = flags.Contains("data"),
            Provider = provider,
            Docker = flags.Contains("docker"),
            Jobs = all || flags.Contains("jobs"),
            Mail = all || flags.Contains("mail"),
            Cache = all || flags.Contains("cache"),
            Outbox = all || flags.Contains("outbox"),
            Push = all || flags.Contains("push"),
            Snapshots = (all || flags.Contains("snapshots")) && DatabaseCatalog.For(provider).IsFileBased,
            Logs = all || flags.Contains("logs"),
            Ops = all || flags.Contains("ops"),
        };
    }

    /// <summary>
    /// Walk the interactive first-run flow (name → template → applicable feature flags) and return the
    /// equivalent argument list, so the answers flow back through the exact same validation and generation
    /// path as a fully-typed command line. Only reached on a terminal.
    /// </summary>
    private static IReadOnlyList<string> RunWizard(Prompt prompt)
    {
        var name = prompt.Ask("Project name");
        var templateKey = prompt.Select(
            "Template",
            [.. TemplateCatalog.All.Select(t => (t.Key, $"{t.Key} — {t.DisplayName}"))],
            TemplateCatalog.Default.Key);

        var args = new List<string> { name, "--template", templateKey };
        _ = TemplateCatalog.TryGet(templateKey, out var template);

        if (templateKey == "native")
        {
            var host = prompt.Select("Host", [("local", "local — self-hosted app"), ("server", "server — thin client of a Rask server")], "local");
            args.Add("--host");
            args.Add(host);
            return args;
        }

        // Snapshots is asked last, after the database is known: it only applies to a file-based database, and
        // the wizard must never let someone assemble a combination that ExecuteAsync then rejects.
        foreach (var flag in FeatureFlags.Where(f => f != "snapshots").Where(template.SupportedFlags.Contains))
        {
            if (prompt.Confirm($"Add --{flag}?", @default: false))
            {
                args.Add("--" + flag);
            }
        }

        var database = DatabaseCatalog.Default;
        if (args.Any(DataImplyingFlags.Contains))
        {
            var databaseKey = prompt.Select(
                "Database",
                [.. DatabaseCatalog.All.Select(d => (d.Key, $"{d.Key} — {d.DisplayName}"))],
                DatabaseCatalog.Default.Key);

            args.Add("--database");
            args.Add(databaseKey);
            _ = DatabaseCatalog.TryGet(databaseKey, out database);
        }

        if (database.IsFileBased
            && template.SupportedFlags.Contains("snapshots")
            && !args.Contains("--all-batteries", StringComparer.Ordinal)
            && prompt.Confirm("Add --snapshots?", @default: false))
        {
            args.Add("--snapshots");
        }

        return args;
    }

    /// <summary>
    /// The flags that pull in a database, so the wizard knows when the <c>--database</c> question is worth
    /// asking. Mirrors the implications <see cref="ServerBatteries.Normalized"/> applies.
    /// </summary>
    private static readonly string[] DataImplyingFlags =
        ["--data", "--jobs", "--mail", "--cache", "--outbox", "--ops", "--all-batteries"];

    private async Task<int> GenerateDirectAsync(
        TemplateInfo template, string name, string? output, bool dryRun, bool force, bool noRestore,
        Func<string, string, ScaffoldResult> build, CancellationToken cancellationToken)
    {
        // rask new MyApp → ./MyApp/ ; --output overrides the destination directory.
        var targetDirectory = Scaffold.TargetDirectory(_workingDirectory, output, name);

        // build() is pure (in-memory strings), so it's safe to run before the existence check — we need its
        // RestoreTarget to know what to guard/restore. Single-project templates restore {name}.csproj at the
        // root; a multi-project template (wasm-hosted) has no root csproj and restores its {name}.sln instead.
        var version = ResolvePackageVersion(CliMetadata.Version);
        var result = build(targetDirectory, version);

        // --dry-run previews the plan without touching disk or restoring — same spirit as `rask generate --dry-run`.
        if (dryRun)
        {
            WriteHeading($"Would create {template.DisplayName} '{name}':");
            foreach (var file in result.Files)
            {
                Console.Out.WriteLine($"  [dry-run] would write {Path.GetRelativePath(_workingDirectory, file.Path)}");
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
                Console.Error.WriteLine($"A project already exists at '{targetDirectory}' ({existing}). Choose another name, --output, or pass --force.");
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

                Console.Error.WriteLine("Choose another name or --output, or pass --force to overwrite.");
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
            Console.Error.WriteLine("Run 'dotnet restore' there once you're online, or re-run with --no-restore to skip this step.");
            return 1;
        }

        return 0;
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
