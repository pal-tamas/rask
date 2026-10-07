using Rask.Cli.Dev;
using Rask.Cli.Scaffolding;
using static Rask.Cli.Commands.DotnetWatchInvocation;

namespace Rask.Cli.Commands;

/// <summary>
/// <c>rask dev</c> — run the app in a fast edit loop. Wraps <c>dotnet watch run</c> with the environment
/// and project selection the loop actually needs, so a bare <c>rask dev</c> works in every template
/// <c>rask new</c> produces.
/// </summary>
/// <remarks>
/// The environment overlay is not a convenience. <c>dotnet watch</c> has no <c>--property</c> switch, and
/// the setting that stops a rude edit blocking on an interactive
/// <c>Yes (y) / No (n) / Always (a) / Never (v)</c> prompt is the MSBuild property
/// <c>HotReloadAutoRestart</c>, read from its design-time build. MSBuild picks properties up from the
/// environment, so the environment is the only channel. (There is no
/// <c>DOTNET_WATCH_RESTART_ON_RUDE_EDIT</c> — .NET 10 defines exactly three <c>DOTNET_WATCH*</c>
/// variables: <c>DOTNET_WATCH</c>, <c>DOTNET_WATCH_ITERATION</c> and <c>DOTNET_WATCH_SUPPRESS_EMOJIS</c>.)
/// </remarks>
internal sealed partial class DevCommand(
    IConsole console,
    IProcessRunner process,
    IFileSystem fileSystem,
    IBrowserLauncher browser,
    string workingDirectory) : CliCommand(console)
{
    /// <summary>
    ///     How the app learns where to point the browser for build status. Read by <c>MapRask</c> in
    ///     Development and stamped onto the page; the client keeps it from the last page it loaded, which
    ///     is what lets it ask a question after the server it came from has gone away.
    /// </summary>
    internal const string DevStatusEnvironmentVariable = "RASK_DEV_STATUS";

    /// <summary>
    ///     The MSBuild property that marks a dev session. Each Rask package expands it into what that
    ///     package needs (see <see cref="BuildDotnetArguments" />), and the scaffolded VS Code build task
    ///     passes the same property — so an F5 build and a <c>rask dev</c> build cannot drift apart.
    /// </summary>
    internal const string DevSessionProperty = "RaskDevSession";

    private readonly IProcessRunner _process = process;
    private readonly IFileSystem _fileSystem = fileSystem;
    private readonly IBrowserLauncher _browser = browser;
    private readonly string _workingDirectory = workingDirectory;

    public override string Name => "dev";

    public override string Summary => "Run the app with hot reload (dotnet watch).";

    // The shape only — the options are listed once, in the schema below, which --help renders directly.
    public override string Usage => "rask dev [options] [-- <args passed to the app>]";

    public override IReadOnlyList<(string Name, string Description)> Arguments =>
        [("[-- <args>]", "Everything after '--' is passed to your app, not to rask.")];

    public override IReadOnlyList<string> Examples =>
    [
        "rask dev",
        "rask dev --open",
        "rask dev --project src/App",
        "rask dev --urls http://localhost:5000",
        "rask dev -- --my-app-flag",
    ];

    public override ArgumentSchema? OptionSchema => CreateSchema();

    private static ArgumentSchema CreateSchema() =>
        new ArgumentSchema()
            .Option("project", 'p', "path", "Project to run (default: the project in the current directory).")
            .Option("urls", valueHint: "url[;url]", description: "URLs the app should listen on (sets ASPNETCORE_URLS).")
            .Option("launch-profile", valueHint: "name", description: "launchSettings profile to use.")
            // No short name. '-o' is --output CLI-wide (new, db), and it was a *flag* here —
            // so `rask dev -o ./somewhere` silently took the path as a positional instead of rejecting
            // it. A short that is a value on other commands and a boolean on this one is the one collision
            // that fails quietly rather than loudly (#601).
            .Flag("open", description: "Open the app in your browser once it is listening (implied on a .test name).")
            .Flag("no-open", description: "Never open a browser.")
            .Flag("no-hot-reload", description: "Restart on change instead of applying edits live (still watches).")
            .Flag("no-restart", description: "Don't restart by itself on an edit hot reload can't apply — ask first.")
            .Flag("once", description: "Run once without watching (plain 'dotnet run').")
            .Flag("no-banner", description: "Suppress the startup banner.")
            .Flag("no-host", description: "Serve on localhost instead of this project's https://<name>.test address.")
            .Flag("dry-run", description: "Print the command that would run without starting anything.")
            .WithJson();

    public override async Task<int> ExecuteAsync(IReadOnlyList<string> args, CancellationToken cancellationToken)
    {
        var parsed = CreateSchema().Parse(args);
        if (parsed.HasErrors)
        {
            return Fail(parsed.Errors);
        }

        var asJson = parsed.HasFlag("json");
        if (asJson && !parsed.HasFlag("dry-run"))
        {
            return Fail(JsonOutput.DryRunOnly(Name));
        }

        var target = DetectTarget(parsed, announce: !asJson);
        if (target is null)
        {
            return 1;
        }

        var once = parsed.HasFlag("once");
        var restartOnRudeEdit = !parsed.HasFlag("no-restart");

        // Without a terminal, watch's rude-edit prompt has nobody to ask and blocks forever.
        var nonInteractive = Console.IsInputRedirected;

        var dotnetArgs = BuildDotnetArguments(
            target.ProjectPath, once, parsed.HasFlag("no-hot-reload"),
            parsed.Option("launch-profile"), nonInteractive, parsed.Passthrough, target.Kind);

        var environment = BuildEnvironment(
            target.Kind, restartOnRudeEdit && !once, parsed.Option("urls"), Environment.GetEnvironmentVariable,
            target.IslandDevServerUrl,
            once);

        // The environment overlay is not incidental here (see the remarks on this class): the MSBuild
        // property that stops a rude edit blocking on an interactive prompt travels through it, so a dry
        // run that showed only the command line would hide the half people actually come asking about.
        if (parsed.HasFlag("dry-run"))
        {
            WriteDryRunPlan(target, dotnetArgs, environment, asJson);
            return 0;
        }

        // Before the banner, because it decides what the banner says the app is reachable on — and after
        // the dry run, which prints a plan and must never change the machine to do it.
        var devHost = await TryPrepareDevHostAsync(target, parsed, nonInteractive, cancellationToken).ConfigureAwait(false);
        if (devHost is not null)
        {
            environment = Overlay(environment, devHost.Environment);
        }

        if (!parsed.HasFlag("no-banner") && !Console.IsOutputRedirected)
        {
            WriteBanner(target, once, parsed.HasFlag("no-hot-reload"), restartOnRudeEdit, parsed.Option("urls"), devHost?.Url);
        }

        var open = ResolveBrowserOpen(target, parsed.HasFlag("open"), parsed.HasFlag("no-open"), parsed.Option("urls"), devHost?.Url);
        var opening = open is null ? Task.CompletedTask : OpenWhenListeningAsync(open, cancellationToken);

        return await RunSessionAsync(target, dotnetArgs, environment, once, opening, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The project to run, or null (with the reason reported) when there is none.</summary>
    private DevTarget? DetectTarget(ParsedArguments parsed, bool announce)
    {
        var target = DevTarget.Detect(_fileSystem, _workingDirectory, parsed.Option("project"));
        if (target is null)
        {
            Console.WriteErrorLine(
                $"{ProjectLocator.DescribeMissing(_fileSystem, _workingDirectory)} Run this inside a project, or pass --project.",
                ConsoleStyle.Error);
            return null;
        }

        if (announce && target.Kind == DevTemplateKind.WasmHosted && parsed.Option("project") is null)
        {
            Console.WriteLine($"Using {target.Name} (the host project).", ConsoleStyle.Dim);
        }

        return target;
    }

    private void WriteDryRunPlan(
        DevTarget target, IReadOnlyList<string> dotnetArgs, IReadOnlyDictionary<string, string> environment, bool asJson)
    {
        if (asJson)
        {
            JsonOutput.Write(Console, DryRunReport(target, dotnetArgs, environment), CliJsonContext.Default.DevDryRunReport);
            return;
        }

        WriteDryRun("run", $"dotnet {string.Join(' ', dotnetArgs)}");
        WriteDryRun("run it in", target.ProjectDirectory);
        foreach (var (key, value) in environment.OrderBy(e => e.Key, StringComparer.Ordinal))
        {
            WriteDryRun("set", $"{key}={value}");
        }
    }

    /// <summary>The dry-run plan as the <c>--json</c> document, its environment in the order the text plan lists it.</summary>
    private static DevDryRunReport DryRunReport(
        DevTarget target, IReadOnlyList<string> dotnetArgs, IReadOnlyDictionary<string, string> environment) =>
        new("dotnet", dotnetArgs, target.ProjectDirectory, new SortedDictionary<string, string>(environment.ToDictionary(StringComparer.Ordinal), StringComparer.Ordinal));

    /// <summary>
    ///     Runs the host under watch (or once), with the client and island dev servers beside it, and waits
    ///     for <paramref name="opening" /> before returning.
    /// </summary>
    private async Task<int> RunSessionAsync(
        DevTarget target, IReadOnlyList<string> dotnetArgs, IReadOnlyDictionary<string, string> environment, bool once,
        Task opening, CancellationToken cancellationToken)
    {
        // The build-status channel (#603). A failed rebuild leaves the app process DOWN, so the browser
        // sees a socket close and — with nothing else to go on — reports a network problem, offering a
        // "Retry now" that can never succeed. Nothing inside the app can tell it otherwise, because the
        // app is what died. This endpoint outlives each rebuild and answers the question instead.
        //
        // `once` runs a plain `dotnet run` with no watching, so there is no rebuild to report on.
        var watcher = new DevBuildWatcher();
        using var status = once ? null : DevStatusServer.TryStart(watcher);

        // Overlaid here rather than in BuildEnvironment because the port is only known once the
        // listener is bound, and BuildEnvironment is a pure function the dry run prints.
        var runEnvironment = status is null
            ? environment
            : Overlay(environment, [new(DevStatusEnvironmentVariable, status.Url)]);

        // The bundler's dev server, beside the host. Its own token, so the host exiting takes it with it —
        // a Vite left listening on 5173 after `rask dev` returns is picked up by the NEXT session, which
        // then serves a stale client against a new server and looks like a Rask bug.
        using var clientTokens = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var client = StartClientDevServer(target, clientTokens.Token);
        var islands = once ? Task.CompletedTask : StartIslandDevServer(target, clientTokens.Token);

        var exit = status is null
            ? await _process
                .RunAsync("dotnet", dotnetArgs, target.ProjectDirectory, cancellationToken, runEnvironment)
                .ConfigureAwait(false)
            : await _process
                .RunTeeAsync("dotnet", dotnetArgs, target.ProjectDirectory, watcher.Observe, cancellationToken,
                    runEnvironment)
                .ConfigureAwait(false);

        await clientTokens.CancelAsync().ConfigureAwait(false);
        await client.ConfigureAwait(false);
        await islands.ConfigureAwait(false);
        await opening.ConfigureAwait(false);
        return exit;
    }

    /// <summary>
    ///     Where Vite listens by default, for a scaffold too old to have baked the real answer into its
    ///     csproj. Not probed from the running bundler, which is not up yet when this is decided.
    /// </summary>
    internal const string ViteDevServerUrl = LocalDevServers.Vite;

    private void WriteBanner(
        DevTarget target,
        bool once,
        bool noHotReload,
        bool restartOnRudeEdit,
        string? urls,
        string? devHostUrl = null)
    {
        WriteHeading($"Rask dev — {target.Name} ({Describe(target)})");

        // The dev host wins: when one is set up it is the address the app is actually reachable on, and
        // the launch profile still names the localhost URL nothing is listening on any more.
        var url = devHostUrl ?? FirstUrl(urls) ?? target.LaunchUrl;
        if (url is not null)
        {
            Console.WriteLine($"  {url}", ConsoleStyle.Code);
        }
        else
        {
            Console.WriteLine("  URL printed by dotnet watch below.", ConsoleStyle.Dim);
        }

        if (once)
        {
            Console.WriteLine("  Running once — not watching for changes.", ConsoleStyle.Dim);
        }
        else if (noHotReload)
        {
            Console.WriteLine("  Watching for changes; the app restarts on save (no live apply).", ConsoleStyle.Dim);
        }
        else
        {
            Console.WriteLine("  Hot reload on. Edits to Render(), scoped .css/.ts apply live.", ConsoleStyle.Dim);
            Console.WriteLine(
                restartOnRudeEdit
                    ? "  Edits it can't apply restart the app automatically (--no-restart to be asked)."
                    : "  Edits it can't apply will ask before restarting.",
                ConsoleStyle.Dim);
        }

        Console.WriteLine("  Ctrl+C to stop.", ConsoleStyle.Dim);
        Console.Out.WriteLine();
    }

    private static string Describe(DevTarget target) => target.Kind switch
    {
        DevTemplateKind.Server => "server",
        DevTemplateKind.WasmHosted => "wasm-hosted",
        DevTemplateKind.SpaHosted => "spa",
        DevTemplateKind.WasmStandalone => "wasm",
        _ => "app"
    };
}
