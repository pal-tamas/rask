using Rask.Cli.Scaffolding;
using Rask.Cli.Templates;

namespace Rask.Cli.Commands;

/// <summary>
/// <c>rask new</c> — scaffold a Rask project. The CLI is the scaffolding authority: the <c>server</c>,
/// <c>wasm</c> and <c>native</c> templates are generated directly (files written + package refs baked at the
/// CLI's own version + <c>dotnet restore</c>), with no <c>dotnet new</c> / Rask.Templates dependency. The one
/// remaining template (<c>wasm-hosted</c>) still delegates to <c>dotnet new</c> until its generator lands.
/// </summary>
internal sealed class NewCommand(IConsole console, IFileSystem fileSystem, IProcessRunner process, string workingDirectory)
    : CliCommand(console)
{
    private readonly IFileSystem _fileSystem = fileSystem;
    private readonly IProcessRunner _process = process;
    private readonly string _workingDirectory = workingDirectory;

    /// <summary>The opt-in feature flags <c>rask new</c> forwards to a template (as <c>--flag</c>).</summary>
    internal static readonly string[] FeatureFlags = ["auth", "pwa", "cqrs", "docker"];

    // Latest published stable used when the running CLI's own version isn't a resolvable package (a dev/CI
    // build stamps a prerelease like "0.17.1-alpha.0.5+sha" that isn't on NuGet). PR5's INuGetClient will
    // resolve this live; until then a known-good stable keeps generated projects restorable.
    private const string LatestStableFallback = "0.17.0";

    public override string Name => "new";

    public override string Summary => "Create a new Rask project from a template.";

    public override string Usage =>
        "rask new <name> [--template server|wasm|wasm-hosted|native] [--auth] [--pwa] [--cqrs] [--docker] [--host local|server] [--output <dir>]";

    public override async Task<int> ExecuteAsync(IReadOnlyList<string> args, CancellationToken cancellationToken)
    {
        var schema = new ArgumentSchema()
            .Option("template", 't')
            .Option("output", 'o')
            .Option("name", 'n')
            .Option("host")
            .Flag("auth")
            .Flag("pwa")
            .Flag("cqrs")
            .Flag("docker");

        var parsed = schema.Parse(args);
        if (parsed.HasErrors)
        {
            return Fail(parsed.Errors);
        }

        var name = parsed.Option("name") ?? parsed.Positionals.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(name))
        {
            Console.Error.WriteLine("A project name is required.");
            Console.Error.WriteLine($"Usage: {Usage}");
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
                template, name, parsed.Option("output"),
                (dir, version) => ProjectGenerator.GenerateNative(dir, name, host, version),
                cancellationToken).ConfigureAwait(false);
        }

        // The remaining ported web templates are generated directly by the CLI; wasm-hosted still goes through
        // dotnet new + Rask.Templates until its generator lands.
        if (template.Key is "server" or "wasm")
        {
            return await GenerateDirectAsync(
                template, name, parsed.Option("output"),
                (dir, version) =>
                {
                    bool auth = requestedFlags.Contains("auth"), pwa = requestedFlags.Contains("pwa"),
                        cqrs = requestedFlags.Contains("cqrs"), docker = requestedFlags.Contains("docker");
                    return template.Key == "wasm"
                        ? ProjectGenerator.GenerateWasm(dir, name, auth, pwa, docker, version)
                        : ProjectGenerator.GenerateServer(dir, name, auth, pwa, cqrs, docker, version);
                },
                cancellationToken).ConfigureAwait(false);
        }

        return await DelegateToDotnetNewAsync(template, name, parsed.Option("output"), requestedFlags, cancellationToken).ConfigureAwait(false);
    }

    private async Task<int> GenerateDirectAsync(
        TemplateInfo template, string name, string? output,
        Func<string, string, ScaffoldResult> build, CancellationToken cancellationToken)
    {
        // rask new MyApp → ./MyApp/ ; --output overrides the destination directory.
        var targetDirectory = Scaffold.TargetDirectory(_workingDirectory, output, name);
        var csprojPath = Path.Combine(targetDirectory, name + ".csproj");
        if (_fileSystem.FileExists(csprojPath))
        {
            Console.Error.WriteLine($"A project already exists at '{targetDirectory}' ({name}.csproj). Choose another name or --output.");
            return 1;
        }

        var version = ResolvePackageVersion(CliMetadata.Version);
        var result = build(targetDirectory, version);

        Console.Out.WriteLine($"Creating {template.DisplayName} '{name}'…");
        foreach (var file in result.Files)
        {
            var directory = Path.GetDirectoryName(file.Path);
            if (!string.IsNullOrEmpty(directory))
            {
                _fileSystem.CreateDirectory(directory);
            }

            _fileSystem.WriteAllText(file.Path, file.Content);
            Console.Out.WriteLine($"  + {Path.GetRelativePath(_workingDirectory, file.Path)}");
        }

        // Package refs are already baked into the csproj at the pinned version; restore pulls them so the
        // project builds immediately. A restore failure is a warning — the files are written and correct.
        var restore = await _process.RunAsync("dotnet", ["restore", csprojPath], targetDirectory, cancellationToken).ConfigureAwait(false);
        if (restore != 0)
        {
            Console.Error.WriteLine("  Couldn't restore automatically — run 'dotnet restore' in the project directory.");
        }

        if (!string.IsNullOrEmpty(result.Notes))
        {
            Console.Out.WriteLine();
            Console.Out.WriteLine(result.Notes);
        }

        return 0;
    }

    private async Task<int> DelegateToDotnetNewAsync(
        TemplateInfo template, string name, string? output, IReadOnlyList<string> flags, CancellationToken cancellationToken)
    {
        var dotnetArgs = BuildDotnetNewArguments(template, name, output, flags);

        if (!await TemplateProbe.AreInstalledAsync(_process, cancellationToken).ConfigureAwait(false))
        {
            Console.Out.WriteLine("Rask templates were not found — installing the Rask.Templates package…");
            var install = await _process.RunAsync("dotnet", ["new", "install", "Rask.Templates"], null, cancellationToken).ConfigureAwait(false);
            if (install != 0)
            {
                Console.Error.WriteLine("Failed to install Rask.Templates. Run 'dotnet new install Rask.Templates' and retry.");
                return install;
            }
        }

        Console.Out.WriteLine($"Creating {template.DisplayName} '{name}'…");
        return await _process.RunAsync("dotnet", dotnetArgs, null, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Resolve the package version to pin in a generated project: the CLI's own version when it's a published
    /// stable, else the latest-stable fallback (a dev/CI prerelease isn't on NuGet). Pure — unit-tested directly.
    /// </summary>
    internal static string ResolvePackageVersion(string cliVersion) =>
        !string.IsNullOrEmpty(cliVersion)
        && !cliVersion.Contains('-', StringComparison.Ordinal)
        && cliVersion != "0.0.0"
            ? cliVersion
            : LatestStableFallback;

    /// <summary>Build the <c>dotnet new</c> argument list. Pure and deterministic, so it is unit-tested directly.</summary>
    internal static IReadOnlyList<string> BuildDotnetNewArguments(
        TemplateInfo template, string name, string? output, IReadOnlyList<string> flags)
    {
        var args = new List<string> { "new", template.ShortName, "--name", name };

        if (!string.IsNullOrWhiteSpace(output))
        {
            args.Add("--output");
            args.Add(output);
        }

        foreach (var flag in flags)
        {
            args.Add("--" + flag);
        }

        return args;
    }
}
