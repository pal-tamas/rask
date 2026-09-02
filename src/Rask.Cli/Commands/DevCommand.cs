using Rask.Cli.Dev;
using System.Text.Json;
using Rask.Cli.Scaffolding;

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
internal sealed class DevCommand(
    IConsole console,
    IProcessRunner process,
    IFileSystem fileSystem,
    IBrowserLauncher browser,
    string workingDirectory) : CliCommand(console)
{
    /// <summary>
    ///     How the app learns where to point the browser for build status. Read by <c>UseRask</c> in
    ///     Development and stamped onto the page; the client keeps it from the last page it loaded, which
    ///     is what lets it ask a question after the server it came from has gone away.
    /// </summary>
    internal const string DevStatusEnvironmentVariable = "RASK_DEV_STATUS";

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
            // No short name. '-o' is --output CLI-wide (new, generate, db), and it was a *flag* here —
            // so `rask dev -o ./somewhere` silently took the path as a positional instead of rejecting
            // it. A short that is a value on four commands and a boolean on a fifth is the one collision
            // that fails quietly rather than loudly (#601).
            .Flag("open", description: "Open the app in your browser once it is listening.")
            .Flag("no-open", description: "Never open a browser.")
            .Flag("no-hot-reload", description: "Restart on change instead of applying edits live (still watches).")
            .Flag("no-restart", description: "Ask before restarting on an edit hot reload can't apply.")
            .Flag("once", description: "Run once without watching (plain 'dotnet run').")
            .Flag("no-banner", description: "Suppress the startup banner.")
            .Flag("dry-run", description: "Print the command that would run without starting anything.");

    public override async Task<int> ExecuteAsync(IReadOnlyList<string> args, CancellationToken cancellationToken)
    {
        var parsed = CreateSchema().Parse(args);
        if (parsed.HasErrors)
        {
            return Fail(parsed.Errors);
        }

        var target = DevTarget.Detect(_fileSystem, _workingDirectory, parsed.Option("project"));
        if (target is null)
        {
            Console.WriteErrorLine(
                $"{ProjectLocator.DescribeMissing(_fileSystem, _workingDirectory)} Run this inside a project, or pass --project.",
                ConsoleStyle.Error);
            return 1;
        }

        if (target.Kind == DevTemplateKind.WasmHosted && parsed.Option("project") is null)
        {
            Console.WriteLine($"Using {target.Name} (the host project).", ConsoleStyle.Dim);
        }

        var once = parsed.HasFlag("once");
        var restartOnRudeEdit = !parsed.HasFlag("no-restart");

        // Without a terminal, watch's rude-edit prompt has nobody to ask and blocks forever.
        var nonInteractive = Console.IsInputRedirected;

        var dotnetArgs = BuildDotnetArguments(
            target.ProjectPath, once, parsed.HasFlag("no-hot-reload"),
            parsed.Option("launch-profile"), nonInteractive, parsed.Passthrough, target.Kind,
            target.HasIslands);

        var environment = BuildEnvironment(
            target.Kind, restartOnRudeEdit && !once, parsed.Option("urls"), Environment.GetEnvironmentVariable,
            target.IslandDevServerUrl);

        // The environment overlay is not incidental here (see the remarks on this class): the MSBuild
        // property that stops a rude edit blocking on an interactive prompt travels through it, so a dry
        // run that showed only the command line would hide the half people actually come asking about.
        if (parsed.HasFlag("dry-run"))
        {
            WriteDryRun("run", $"dotnet {string.Join(' ', dotnetArgs)}");
            WriteDryRun("run it in", target.ProjectDirectory);
            foreach (var (key, value) in environment.OrderBy(e => e.Key, StringComparer.Ordinal))
            {
                WriteDryRun("set", $"{key}={value}");
            }

            return 0;
        }

        if (!parsed.HasFlag("no-banner") && !Console.IsOutputRedirected)
        {
            WriteBanner(target, once, parsed.HasFlag("no-hot-reload"), restartOnRudeEdit, parsed.Option("urls"));
        }

        var open = ResolveBrowserOpen(target, parsed.HasFlag("open"), parsed.HasFlag("no-open"), parsed.Option("urls"));
        var opening = open is null ? Task.CompletedTask : OpenWhenListeningAsync(open, cancellationToken);

        // The build-status channel (#603). A failed rebuild leaves the app process DOWN, so the browser
        // sees a socket close and — with nothing else to go on — reports a network problem, offering a
        // "Retry now" that can never succeed. Nothing inside the app can tell it otherwise, because the
        // app is what died. This endpoint outlives each rebuild and answers the question instead.
        //
        // `once` runs a plain `dotnet run` with no watching, so there is no rebuild to report on.
        var watcher = new DevBuildWatcher();
        using var status = once ? null : DevStatusServer.TryStart(watcher);

        var runEnvironment = environment;
        if (status is not null)
        {
            // Overlaid here rather than in BuildEnvironment because the port is only known once the
            // listener is bound, and BuildEnvironment is a pure function the dry run prints.
            var withStatus = new Dictionary<string, string>(environment, StringComparer.Ordinal)
            {
                [DevStatusEnvironmentVariable] = status.Url,
            };
            runEnvironment = withStatus;
        }

        // The bundler's dev server, beside the host. Its own token, so the host exiting takes it with it —
        // a Vite left listening on 5173 after `rask dev` returns is picked up by the NEXT session, which
        // then serves a stale client against a new server and looks like a Rask bug.
        using var clientTokens = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var client = StartClientDevServer(target, clientTokens.Token);
        var islands = StartIslandDevServer(target, clientTokens.Token);

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
    ///     Starts the front end's own dev server for a SPA-hosted app, or does nothing for anything else.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Two processes rather than one because the browser talks to the bundler, not to ASP.NET: that
    ///         is what makes HMR native and instant, and it is why the scaffolded <c>vite.config.ts</c>
    ///         proxies <c>/_rask</c> back to the host. In production neither of those exists — the host
    ///         serves the built bundle and answers the wire itself.
    ///     </para>
    ///     <para>
    ///         A failure here is reported and then let go. The host is the process this command is really
    ///         running, and killing it because a bundler would not start would take away the API too — and
    ///         with it any chance of reading the error against a working server.
    ///     </para>
    /// </remarks>
    private async Task StartClientDevServer(DevTarget target, CancellationToken cancellationToken)
    {
        if (target.Kind != DevTemplateKind.SpaHosted)
        {
            return;
        }

        if (target.ClientDirectory is not { } directory)
        {
            Console.WriteLine(
                "No client directory found beside this host, so no dev server was started. Run the "
                + "bundler yourself, or point RaskSpaClientDir at it.",
                ConsoleStyle.Dim);
            return;
        }

        Console.WriteLine(
            $"Starting the client dev server in {Path.GetFileName(directory)} "
            + $"(npm run {target.ClientDevScript ?? "dev"})…",
            ConsoleStyle.Dim);

        try
        {
            await _process
                .RunAsync("npm", ["run", target.ClientDevScript ?? "dev"], directory, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The host exited and took this with it. Expected, every time.
        }
        catch (System.ComponentModel.Win32Exception)
        {
            Console.WriteErrorLine(
                "npm is not available, so the client dev server did not start. The API is still running. "
                + "Install Node.js from https://nodejs.org.",
                ConsoleStyle.Error);
        }
    }

    /// <summary>
    ///     Serves this project's islands from a Vite dev server, so editing a <c>.tsx</c> or a
    ///     <c>.svelte</c> hot-replaces instead of rebuilding.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Without this an island edit goes through the whole MSBuild path: <c>dotnet watch</c> sees
    ///         the file, rebuilds, and runs a production <c>vite build</c> over every island in the
    ///         project. Correct, and far too slow to work in — and the page reloads, so whatever state
    ///         the island held is gone.
    ///     </para>
    ///     <para>
    ///         The config it runs is the one the BUILD generated, which is why this waits for it rather
    ///         than computing a path: only MSBuild knows which configuration and target framework were
    ///         chosen, and the pointer file it drops at the stable <c>obj/rask-external/</c> path is how
    ///         it says so. Waiting is also correct on the first run of a clean clone, where the config
    ///         does not exist until the build that is starting right now has written it.
    ///     </para>
    ///     <para>
    ///         A failure here is reported and let go, exactly as for the SPA client: the host is the
    ///         process this command is really running, and losing hot reload is not a reason to take the
    ///         app down with it.
    ///     </para>
    /// </remarks>
    private async Task StartIslandDevServer(DevTarget target, CancellationToken cancellationToken)
    {
        if (!target.HasIslands)
        {
            return;
        }

        var pointer = Path.Combine(target.ProjectDirectory, "obj", "rask-external", "dev.json");

        var config = await WaitForIslandConfig(pointer, cancellationToken).ConfigureAwait(false);
        if (config is null)
        {
            return;
        }

        Console.WriteLine($"Serving islands from {config.Value.Url} (hot reload)…", ConsoleStyle.Dim);

        try
        {
            await _process
                .RunAsync(
                    "npx",
                    ["--no-install", "vite", "--config", config.Value.Config],
                    target.ProjectDirectory,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The host exited and took this with it. Expected, every time.
        }
        catch (System.ComponentModel.Win32Exception)
        {
            Console.WriteErrorLine(
                "npx is not available, so the island dev server did not start. The app is still running, "
                + "but islands will not hot-reload. Install Node.js from https://nodejs.org.",
                ConsoleStyle.Error);
        }
    }

    /// <summary>
    ///     Waits for the build to drop the island dev-server pointer, or gives up.
    /// </summary>
    /// <remarks>
    ///     Polled rather than watched: the file appears exactly once per session, within the first build,
    ///     and a FileSystemWatcher for that is more moving parts than the thing it replaces. The timeout
    ///     is generous because the first build of a clean clone restores and compiles first — and giving
    ///     up quietly is right, since the app itself is running by then and the only thing lost is hot
    ///     reload the user can still get by restarting.
    /// </remarks>
    private static async Task<(string Url, string Config)?> WaitForIslandConfig(
        string pointer, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.AddMinutes(3);

        while (DateTimeOffset.UtcNow < deadline)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return null;
            }

            if (File.Exists(pointer))
            {
                try
                {
                    using var document = JsonDocument.Parse(File.ReadAllText(pointer));
                    var url = document.RootElement.GetProperty("url").GetString();
                    var config = document.RootElement.GetProperty("config").GetString();

                    if (!string.IsNullOrEmpty(url) && !string.IsNullOrEmpty(config) && File.Exists(config))
                    {
                        return (url!, config!);
                    }
                }
                catch (Exception ex) when (ex is IOException or JsonException or KeyNotFoundException)
                {
                    // Half-written, or written by an older Rask. Fall through and look again.
                }
            }

            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return null;
            }
        }

        return null;
    }

    /// <summary>
    /// Build the <c>dotnet watch/run</c> argument list. Pure and deterministic, so it is unit-tested directly.
    /// </summary>
    /// <remarks>
    /// <c>--no-hot-reload</c>, <c>--non-interactive</c> and <c>-lp</c> are <em>watch</em> options and must
    /// precede <c>run</c>; everything after <c>--</c> goes to the app.
    /// </remarks>
    internal static IReadOnlyList<string> BuildDotnetArguments(
        string? project,
        bool once,
        bool noHotReload,
        string? launchProfile,
        bool nonInteractive,
        IReadOnlyList<string> passthrough,
        DevTemplateKind kind = DevTemplateKind.Server,
        bool islands = false)
    {
        var args = new List<string>();

        if (once)
        {
            // The honest "just run it" mode. Note this clears DOTNET_WATCH, which the framework keys its
            // own dev-time behaviour off — so it is opt-in, not what --no-hot-reload means.
            args.Add("run");
            AddProject(args, project);
        }
        else
        {
            args.Add("watch");
            AddProject(args, project);
            if (nonInteractive)
            {
                args.Add("--non-interactive");
            }

            if (noHotReload)
            {
                args.Add("--no-hot-reload");
            }

            if (launchProfile is { Length: > 0 })
            {
                args.Add("-lp");
                args.Add(launchProfile);
            }

            args.Add("run");

            // A wasm-hosted host serves its client's PUBLISHED bundle by default, which is (a) republished
            // by a nested emscripten relink on every save and (b) trimmed — and trimming folds
            // MetadataUpdater.IsSupported to false, so an applied delta could never reach the browser
            // session. This switches it to the client's build output for the watch session. Not passed
            // under --no-hot-reload (nothing to apply) or --once (that mode is deliberately a plain run).
            //
            // `--property:`, not `-p:`: on `dotnet run` the short form is ambiguous with --project.
            if (kind == DevTemplateKind.WasmHosted && !noHotReload)
            {
                args.Add("--property:RaskWasmDevBundle=true");
            }

            // The bundler's own dev server owns the client during a dev session — it is started beside
            // this, and it is what the browser talks to. Paying for a full production bundle on every
            // save as well would make watch unusable, and nothing would ever read the result.
            //
            // The generated TypeScript is emitted anyway: that is deliberately independent of
            // RaskSpaBuild, because a dev server compiling last build's contracts is exactly the failure
            // this whole pipeline exists to prevent.
            if (kind == DevTemplateKind.SpaHosted)
            {
                args.Add("--property:RaskSpaBuild=false");
            }

            // Islands are served by their own Vite dev server for this session, so the production
            // bundle would be a full rebuild of every island on every save that nothing then reads.
            //
            // NOT RaskExternalBuild=false, which turns the feature off outright — no entry modules, no
            // manifest, no prop types, and islands that never mount. This skips exactly the bundling
            // step and leaves the manifest being written, pointing at the dev server.
            if (islands)
            {
                args.Add("--property:RaskExternalDevServer=true");
            }
        }

        if (once && launchProfile is { Length: > 0 })
        {
            args.Add("-lp");
            args.Add(launchProfile);
        }

        if (passthrough.Count > 0)
        {
            args.Add("--");
            args.AddRange(passthrough);
        }

        return args;
    }

    /// <summary>
    /// The environment overlay handed to the child. Pure, with <paramref name="readEnv" /> injected so tests
    /// never touch the real environment.
    /// </summary>
    internal static IReadOnlyDictionary<string, string> BuildEnvironment(
        DevTemplateKind kind,
        bool restartOnRudeEdit,
        string? urls,
        Func<string, string?> readEnv,
        string? islandDevServerUrl = null)
    {
        var env = new Dictionary<string, string>(StringComparer.Ordinal);

        // Keep watch's colour when we read its output. Reading it means redirecting stdout, and .NET
        // disables ANSI the moment stdout is not a terminal — so watch's build output would arrive at the
        // developer's console grey and unstyled, which is a real cost paid for a feature they cannot see.
        // This is the documented opt-out, and it is set unconditionally: `rask dev` is the only caller,
        // its output always ends up on a real console, and a user who genuinely wants plain text sets
        // NO_COLOR, which the SDK honours ahead of this.
        env["DOTNET_SYSTEM_CONSOLE_ALLOW_ANSI_COLOR_REDIRECTION"] = "1";

        // Only when the user has set neither — "when unset" is the whole requirement, and silently
        // overriding someone's Staging run would be worse than not helping at all.
        if (kind != DevTemplateKind.WasmStandalone
            && string.IsNullOrEmpty(readEnv("ASPNETCORE_ENVIRONMENT"))
            && string.IsNullOrEmpty(readEnv("DOTNET_ENVIRONMENT")))
        {
            env["ASPNETCORE_ENVIRONMENT"] = "Development";
        }

        if (restartOnRudeEdit)
        {
            // An MSBuild property, read from watch's design-time build. Environment is the only way in.
            env["HotReloadAutoRestart"] = "true";
        }

        if (urls is { Length: > 0 })
        {
            env["ASPNETCORE_URLS"] = urls;
        }

        // Where the page should load @vite/client from, so each framework's own hot replacement takes
        // over its modules. Stamped onto <body> by the server as data-rask-islands-dev, and only in
        // development — a production page carrying a localhost URL would have every visitor's browser
        // open a websocket to their own machine.
        if (islandDevServerUrl is { Length: > 0 })
        {
            env["RASK_ISLANDS_DEV"] = islandDevServerUrl;
        }

        return env;
    }

    // Which URL, if any, we should open ourselves. Null when nobody should, or when watch is already
    // going to: the profile's own launchBrowser is honoured by dotnet watch and .NET 10 has no
    // environment variable to suppress it, so opening as well would just produce two tabs.
    private string? ResolveBrowserOpen(DevTarget target, bool open, bool noOpen, string? urls)
    {
        if (noOpen || !open)
        {
            return null;
        }

        if (target.ProfileLaunchesBrowser)
        {
            Console.WriteLine(
                "launchSettings.json already opens a browser for this profile — skipping --open. " +
                "Set \"launchBrowser\": false there to change that.",
                ConsoleStyle.Dim);
            return null;
        }

        // For a SPA host the browser belongs on the BUNDLER, not on ASP.NET: the dev server is what serves
        // the app and what HMR reaches, and it proxies the wire back to the host. Opening the host's own
        // port instead lands on "nothing built yet" and looks like a broken scaffold.
        //
        // --urls is still honoured: it names where the HOST listens, and someone who set it deliberately is
        // saying that is the address they mean.
        if (target.Kind == DevTemplateKind.SpaHosted && urls is not { Length: > 0 })
        {
            return target.ClientDevServerUrl ?? ViteDevServerUrl;
        }

        var url = FirstUrl(urls) ?? target.LaunchUrl;
        if (url is null)
        {
            Console.WriteLine("--open: no URL to open (no launch profile and no --urls).", ConsoleStyle.Dim);
        }

        return url;
    }

    /// <summary>
    ///     Where Vite listens by default, for a scaffold too old to have baked the real answer into its
    ///     csproj. Not probed from the running bundler, which is not up yet when this is decided.
    /// </summary>
    internal const string ViteDevServerUrl = "http://localhost:5173";

    private async Task OpenWhenListeningAsync(string url, CancellationToken cancellationToken)
    {
        // Poll until something answers, then open exactly once. Never fails the run.
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);

        while (DateTime.UtcNow < deadline && !cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var response = await http.GetAsync(url, cancellationToken).ConfigureAwait(false);
                if ((int)response.StatusCode < 500)
                {
                    await _browser.TryOpenAsync(url, cancellationToken).ConfigureAwait(false);
                    return;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception)
            {
                // Not listening yet.
            }

            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(400), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private void WriteBanner(DevTarget target, bool once, bool noHotReload, bool restartOnRudeEdit, string? urls)
    {
        WriteHeading($"Rask dev — {target.Name} ({Describe(target.Kind)})");

        var url = FirstUrl(urls) ?? target.LaunchUrl;
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

    private static string Describe(DevTemplateKind kind) => kind switch
    {
        DevTemplateKind.Server => "server",
        DevTemplateKind.WasmHosted => "wasm-hosted",
        DevTemplateKind.SpaHosted => "react",
        DevTemplateKind.WasmStandalone => "wasm",
        _ => "app"
    };

    private static string? FirstUrl(string? urls) =>
        urls?.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();

    private static void AddProject(List<string> args, string? project)
    {
        if (!string.IsNullOrWhiteSpace(project))
        {
            args.Add("--project");
            args.Add(project);
        }
    }
}
