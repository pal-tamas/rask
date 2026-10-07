using System.Globalization;
using Rask.Cli.Scaffolding;
using Rask.Cli.Templates;
using static Rask.Cli.Commands.BatterySelection;
using static Rask.Cli.Commands.NewArgumentChecks;

namespace Rask.Cli.Commands;

/// <summary>
/// <c>rask new</c> — scaffold a Rask project. The CLI is the scaffolding authority: every template
/// (<c>server</c>, <c>wasm</c>, and the front-end ones) is generated directly — files written +
/// package refs baked at the CLI's own version + <c>dotnet restore</c> — with no <c>dotnet new</c> /
/// Rask.Templates dependency.
/// </summary>
internal sealed partial class NewCommand(IConsole console, IFileSystem fileSystem, IProcessRunner process, string workingDirectory)
    : CliCommand(console)
{
    private readonly IFileSystem _fileSystem = fileSystem;
    private readonly IProcessRunner _process = process;
    private readonly string _workingDirectory = workingDirectory;

    /// <summary>The template key for a Rask WebAssembly front end on an ASP.NET host.</summary>
    /// <remarks>
    /// Named once and shared: a literal spelled out at each of the places that ask about it is how a key
    /// comes to be accepted by the parser and then generate something else.
    /// </remarks>
    internal const string WasmHostedKey = "wasm-hosted";

    public override string Name => "new";

    public override string Summary => "Create a new Rask project from a template.";

    // The shape only — the flags are listed once, in the schema below, which --help renders directly.
    public override string Usage => "rask new <name> [options]";

    public override IReadOnlyList<(string Name, string Description)> Arguments =>
        [("<name>", "Name of the project to create (scaffolds ./<name>/).")];

    public override IReadOnlyList<string> Examples =>
    [
        "rask new Shop",
        "rask new Shop --template wasm-hosted",
        "rask new Shop --template react",
        "rask new Shop --islands react angular",
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
            .MultiOption(
                "islands",
                valueHint: "runtime",
                description:
                    "Scaffold a front-end component as an ordinary Rask component, one per runtime named "
                    + "(react, preact, vue, svelte, solid, lit, angular, blazor). Takes several: "
                    + "`--islands react angular`. react and preact cannot share a project.",
                choices: IslandRuntimes.All)
            .Flag("no-pwa", description: "Leave out the PWA manifest, icon, and offline page (also drops Web Push).")
            .Flag("no-push", description: "Leave out server-sent Web Push and its subscribe endpoints.")
            .Flag("no-cqrs", description: "Leave out Rask.Cqrs — and with it the database, whose writes, jobs and domain events all go through it.")
            .Flag("no-data", description: "Leave out the database and EF Core — and with it every battery that maps onto a DbContext.")
            .Flag("no-jobs", description: "Leave out durable background jobs.")
            .Flag("no-mail", description: "Leave out transactional email.")
            .Flag("no-cache", description: "Leave out the database-backed ICache + IDistributedCache.")
            .Flag("no-storage", description: "Leave out file storage for uploads (IFiles) and its StoredFile table.")
            .Flag("no-snapshots", description: "Leave out scheduled point-in-time SQLite backups.")
            .Flag("no-logs", description: "Leave out the durable log store (it keeps a database of its own).")
            .Flag("no-ops", description: "Leave out the operator dashboard at /_rask.")
            .Flag("no-docker", description: "Leave out the Dockerfile and .dockerignore.")
            .Flag("no-tests", description: "Leave out the <name>.Tests project and its first passing test.")
            .Flag("no-restore", description: "Don't run dotnet restore after scaffolding (for offline use). Also skips the first migration.")
            .Flag("no-git", description: "Don't initialize a git repository (one is created with an initial commit by default).")
            .Flag("force", description: "Scaffold into a directory that already has files in it, overwriting on collision.")
            .Flag("dry-run", description: "Print the files that would be written without touching disk.")
            .WithJson();

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

        if (parsed.HasFlag("json") && !parsed.HasFlag("dry-run"))
        {
            return Fail(JsonOutput.DryRunOnly(Name));
        }

        if (NameArgumentError(parsed) is { } nameError)
        {
            return Fail(nameError);
        }

        var name = parsed.Option("name") ?? parsed.FirstPositional;
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

        return await ScaffoldAsync(parsed, name, cancellationToken).ConfigureAwait(false);
    }

    private async Task<int> ScaffoldAsync(ParsedArguments parsed, string name, CancellationToken cancellationToken)
    {
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
        if (BatteryFlagError(template, off) is { } batteryError)
        {
            return Fail(batteryError);
        }

        var batteries = ToBatteries(template, off);

        var islands = parsed.MultiOption("islands");
        if (IslandsError(template, islands) is { } islandsError)
        {
            return Fail(islandsError);
        }

        // Every template is generated directly by the CLI; the key here is one the catalog knows
        // (validated by TemplateCatalog.TryGet).
        return await GenerateDirectAsync(
            template, name, parsed.Option("output"), parsed.HasFlag("dry-run"), parsed.HasFlag("force"),
            parsed.HasFlag("no-restore"), parsed.HasFlag("no-git"), parsed.HasFlag("json"), batteries,
            (dir, version) => Generate(template, dir, name, batteries, version, islands),
            cancellationToken).ConfigureAwait(false);
    }

    private static ScaffoldResult Generate(
        TemplateInfo template, string dir, string name, ServerBatteries batteries, string version,
        IReadOnlyList<string> islands)
    {
        // Asked of the same list the catalog was built from, so a key the parser accepts is one this generates.
        if (SpaFramework.TryGet(template.Key, out var framework))
        {
            return ProjectGenerator.GenerateSpa(dir, name, framework, batteries, version);
        }

        return template.Key switch
        {
            "wasm" => ProjectGenerator.GenerateWasm(
                dir, name, batteries.Pwa, batteries.Docker, version, batteries, islands),
            WasmHostedKey => ProjectGenerator.GenerateWasmHosted(
                dir, name, batteries, version, islands),
            _ => ProjectGenerator.GenerateServer(dir, name, batteries, version, islands),
        };
    }

    /// <summary>
    ///     MSBuild reads properties from the environment, which is how these reach a build whose command
    ///     line belongs to <c>dotnet-ef</c>. Same channel <c>rask dev</c> uses for
    ///     <c>HotReloadAutoRestart</c>, and it leaves <c>rask db</c>'s argument surface alone.
    /// </summary>
    private static readonly Dictionary<string, string> SkipFrontEndBuild = new(StringComparer.Ordinal)
    {
        ["RaskSpaBuild"] = "false",
    };

    /// <summary>
    ///     The package feed <c>rask new</c> checks the pinned versions against before restoring, or <c>null</c> to skip the
    ///     check. Set by <c>CliApplication</c>; left unset in tests, so none of them reaches the network.
    /// </summary>
    internal PackageFeed? Feed { get; init; }

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
        if (string.IsNullOrEmpty(cliVersion) || string.Equals(cliVersion, "0.0.0", StringComparison.Ordinal))
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
