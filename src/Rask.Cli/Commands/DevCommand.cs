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
    private readonly IProcessRunner _process = process;
    private readonly IFileSystem _fileSystem = fileSystem;
    private readonly IBrowserLauncher _browser = browser;
    private readonly string _workingDirectory = workingDirectory;

    public override string Name => "dev";

    public override string Summary => "Run the app with hot reload (dotnet watch).";

    public override string Usage => "rask dev [--project <path>] [--open] [-- <args passed to the app>]";

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
            .Flag("open", 'o', "Open the app in your browser once it is listening.")
            .Flag("no-open", description: "Never open a browser.")
            .Flag("no-hot-reload", description: "Restart on change instead of applying edits live (still watches).")
            .Flag("no-restart", description: "Ask before restarting on an edit hot reload can't apply.")
            .Flag("once", description: "Run once without watching (plain 'dotnet run').")
            .Flag("no-banner", description: "Suppress the startup banner.");

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
            Console.Error.WriteLine(
                $"Couldn't find a single .csproj at or above '{_workingDirectory}'. Run this inside a project, or pass --project.");
            return 1;
        }

        if (target.Kind == DevTemplateKind.Native)
        {
            return RefuseNative(target);
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
            parsed.Option("launch-profile"), nonInteractive, parsed.Passthrough);

        var environment = BuildEnvironment(
            target.Kind, restartOnRudeEdit && !once, parsed.Option("urls"), Environment.GetEnvironmentVariable);

        if (!parsed.HasFlag("no-banner") && !Console.IsOutputRedirected)
        {
            WriteBanner(target, once, parsed.HasFlag("no-hot-reload"), restartOnRudeEdit, parsed.Option("urls"));
        }

        var open = ResolveBrowserOpen(target, parsed.HasFlag("open"), parsed.HasFlag("no-open"), parsed.Option("urls"));
        var opening = open is null ? Task.CompletedTask : OpenWhenListeningAsync(open, cancellationToken);

        var exit = await _process
            .RunAsync("dotnet", dotnetArgs, target.ProjectDirectory, cancellationToken, environment)
            .ConfigureAwait(false);

        await opening.ConfigureAwait(false);
        return exit;
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
        IReadOnlyList<string> passthrough)
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
        Func<string, string?> readEnv)
    {
        var env = new Dictionary<string, string>(StringComparer.Ordinal);

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

        var url = FirstUrl(urls) ?? target.LaunchUrl;
        if (url is null)
        {
            Console.WriteLine("--open: no URL to open (no launch profile and no --urls).", ConsoleStyle.Dim);
        }

        return url;
    }

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
            Console.WriteLine("  Hot reload on. Edits to Render(), scoped .css/.js apply live.", ConsoleStyle.Dim);
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
        DevTemplateKind.WasmStandalone => "wasm",
        DevTemplateKind.Native => "native",
        _ => "app"
    };

    private int RefuseNative(DevTarget target)
    {
        // Exit 1, not 2: the command line was well-formed, the target is simply wrong for it.
        // Don't guess a TFM — picking the wrong one costs a ten-minute build for the wrong platform.
        Console.Error.WriteLine(
            $"'{target.Name}' is a Rask native app. `rask dev` runs dotnet watch, which can't drive a " +
            "simulator or emulator. Run it on a device instead:");
        Console.Error.WriteLine();
        foreach (var line in NativeRunCommands.Lines)
        {
            Console.Error.WriteLine($"  {line}");
        }

        Console.Error.WriteLine();
        Console.Error.WriteLine("If you meant a different project, pass --project.");
        return 1;
    }

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

/// <summary>
/// The on-device run commands for a native app. Shared so the <c>rask dev</c> refusal and the
/// <c>rask new</c> next-steps text cannot drift apart.
/// </summary>
internal static class NativeRunCommands
{
    public const string Android = "dotnet build -t:Run -f net10.0-android";
    public const string IOS = "dotnet build -t:Run -f net10.0-ios";

    public static IReadOnlyList<string> Lines =>
    [
        $"{Android}     # Android emulator",
        $"{IOS}         # iOS simulator (macOS + Xcode)"
    ];
}
