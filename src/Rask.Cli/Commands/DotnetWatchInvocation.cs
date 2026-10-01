using static Rask.Cli.Commands.DevCommand;

namespace Rask.Cli.Commands;

/// <summary>The <c>dotnet watch</c> command line and environment <c>rask dev</c> starts, built without side effects.</summary>
internal static class DotnetWatchInvocation
{
    internal static Dictionary<string, string> Overlay(
        IReadOnlyDictionary<string, string> environment, IEnumerable<KeyValuePair<string, string>> overrides)
    {
        var merged = new Dictionary<string, string>(environment, StringComparer.Ordinal);
        foreach (var (key, value) in overrides)
        {
            merged[key] = value;
        }

        return merged;
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
        DevTemplateKind kind = DevTemplateKind.Server)
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
            AddWatchArguments(args, project, noHotReload, launchProfile, nonInteractive, kind);
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
        string? islandDevServerUrl = null,
        bool once = false)
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
        //
        // Never under --once. BuildDotnetArguments already withholds the MSBuild property there, so
        // that mode serves a real bundle with no dev server beside it; stamping the page anyway would
        // leave it importing @vite/client from a port nothing is listening on. Decided HERE rather
        // than at the call site so the rule is pinned by a test.
        if (!once && islandDevServerUrl is { Length: > 0 })
        {
            env["RASK_ISLANDS_DEV"] = islandDevServerUrl;
        }

        return env;
    }

    internal static string? FirstUrl(string? urls) =>
        urls?.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();

    /// <summary>The <c>dotnet watch … run</c> half of <see cref="BuildDotnetArguments" />.</summary>
    internal static void AddWatchArguments(
        List<string> args, string? project, bool noHotReload, string? launchProfile, bool nonInteractive, DevTemplateKind kind)
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

        // ONE switch for the whole dev session, expanded by each referenced package's own props into
        // what that package needs — and ignored by every package the project does not reference:
        //
        //   • Rask.Spa.Hosting serves a WebAssembly client's BUILD output. The published bundle is republished
        //     by a nested emscripten relink on every save, and it is trimmed — trimming folds
        //     MetadataUpdater.IsSupported to false, so an applied delta could never reach the page.
        //   • Rask.External serves islands from a Vite dev server instead of bundling them. NOT
        //     RaskExternalBuild=false, which turns the feature off outright and leaves islands that
        //     never mount.
        //
        // The scaffolded VS Code build task passes the very same property, which is what keeps an F5
        // session and this one from drifting apart. Not passed under --once, which is deliberately a
        // plain run against a real build.
        //
        // `--property:`, not `-p:`: on `dotnet run` the short form is ambiguous with --project.
        args.Add($"--property:{DevSessionProperty}=true");

        // Under --no-hot-reload there is nothing to apply, so a wasm-hosted app serves its published
        // bundle as it always did. Explicit, because an explicit value beats the dev session's.
        if (kind == DevTemplateKind.WasmHosted && noHotReload)
        {
            args.Add("--property:RaskSpaBuild=true");
        }
    }

    internal static void AddProject(List<string> args, string? project)
    {
        if (!string.IsNullOrWhiteSpace(project))
        {
            args.Add("--project");
            args.Add(project);
        }
    }
}
