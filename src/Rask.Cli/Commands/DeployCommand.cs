using System.Globalization;
using System.Text;
using Rask.Cli.Scaffolding;

namespace Rask.Cli.Commands;

/// <summary>
/// <c>rask deploy</c> — build the app's Docker image on a single host over SSH and run it, and (when a
/// <c>--domain</c> is given) front it with a shared Caddy reverse proxy that fetches an automatic
/// Let's Encrypt certificate. Every remote operation is <c>docker -H ssh://user@host …</c>, so there's no
/// registry, no local daemon, and no image tarball — the build context ships to the box's daemon and
/// builds there.
///
/// <para>Multiple apps coexist on one box: each app container carries <c>rask.*</c> labels, so the box is
/// self-describing and the shared proxy's Caddyfile is regenerated from the live containers on every
/// deploy. The domain path is blue-green: the new container starts alongside the old and is waited on until
/// its container is <c>Running</c>, Caddy is reloaded to point at it, then the old container is removed —
/// so a container that fails to start never takes traffic. (v1 gates on the container running, not an
/// app-level HTTP health check; that's a planned follow-up.)</para>
/// </summary>
internal sealed class DeployCommand(IConsole console, IFileSystem fileSystem, IProcessRunner process, string workingDirectory)
    : CliCommand(console)
{
    /// <summary>The container port the scaffolded server/wasm-hosted Dockerfiles listen on (<c>EXPOSE 8080</c>).</summary>
    internal const int ContainerPort = 8080;

    /// <summary>The shared docker network app containers and the Caddy proxy join in domain mode.</summary>
    internal const string Network = "rask";

    /// <summary>The shared reverse-proxy container name (one per host, routes every app's domain).</summary>
    internal const string CaddyContainer = "rask-caddy";

    private readonly IFileSystem _fileSystem = fileSystem;
    private readonly IProcessRunner _process = process;
    private readonly string _workingDirectory = workingDirectory;

    /// <summary>How long to wait between readiness polls, and how many — overridden to zero in tests.</summary>
    internal TimeSpan ReadinessDelay { get; set; } = TimeSpan.FromSeconds(2);

    internal int ReadinessAttempts { get; set; } = 10;

    public override string Name => "deploy";

    public override string Summary => "Build and deploy the app to a single host over SSH (auto-HTTPS with --domain).";

    public override string Usage =>
        "rask deploy [--host user@box] [--domain app.example.com] [--port <n>] [--project <path>] " +
        "[--name <slug>] [--dockerfile <path>] [--env KEY=VALUE ...] [--env-file <path>] [--dry-run]";

    public override IReadOnlyList<string> Examples =>
    [
        "rask deploy --host deploy@box.example.com --domain app.example.com",
        "rask deploy --host deploy@box.example.com --port 8080",
        "rask deploy --env ConnectionStrings__Db=... --env-file .env.production",
        "rask deploy --dry-run",
    ];

    public override ArgumentSchema? OptionSchema => CreateSchema();

    private static ArgumentSchema CreateSchema() =>
        new ArgumentSchema()
            .Option("host", 'h', "user@box", "SSH target to build and run on (remembered in .rask/deploy.json).")
            .Option("domain", 'd', "host", "Public domain to serve over HTTPS via Caddy (implies ports 80/443).")
            .Option("port", valueHint: "n", description: "Published port when not using --domain (default: 8080).")
            .Option("project", 'p', "path", "Project to deploy (default: found from the current directory).")
            .Option("name", 'n', "slug", "Container/app name (default: derived from the project).")
            .Option("dockerfile", valueHint: "path", description: "Dockerfile to build (default: ./Dockerfile).")
            .Option("env-file", valueHint: "path", description: "File of KEY=VALUE lines to pass to the container.")
            .MultiOption("env", 'e', "KEY=VALUE", "Environment variable to pass (repeatable).")
            .Flag("dry-run", description: "Print the docker commands that would run without changing anything.");

    public override async Task<int> ExecuteAsync(IReadOnlyList<string> args, CancellationToken cancellationToken)
    {
        var schema = CreateSchema();

        var parsed = schema.Parse(args);
        if (parsed.HasErrors)
        {
            return Fail(parsed.Errors);
        }

        if (parsed.Positionals.Count > 0)
        {
            Console.Error.WriteLine($"Unexpected argument '{parsed.Positionals[0]}'. Usage: {Usage}");
            return 1;
        }

        // Flags win over the persisted config; anything unset falls back to .rask/deploy.json.
        var config = DeployConfig.Load(_fileSystem, _workingDirectory);
        var host = Normalize(parsed.Option("host") ?? config.Host);
        var domain = Normalize(parsed.Option("domain") ?? config.Domain);
        var envFile = parsed.Option("env-file") ?? config.EnvFile;
        var dryRun = parsed.HasFlag("dry-run");

        if (!TryResolvePort(parsed.Option("port"), config.Port, out var port, out var portError))
        {
            Console.Error.WriteLine(portError);
            return 1;
        }

        // --port and --domain are two different modes: with a domain the app is reached internally on the
        // proxy network, so a published host port is meaningless. Reject the combination explicitly rather
        // than silently ignore --port (which also covers a --port passed against a remembered domain).
        if (parsed.Option("port") is not null && domain is not null)
        {
            Console.Error.WriteLine(parsed.Option("domain") is not null
                ? "--port doesn't apply with --domain (the app is served over HTTPS on 80/443 via the proxy)."
                : $"This app is deployed with --domain {domain} (remembered in .rask/deploy.json), so --port doesn't apply. Remove \"domain\" from .rask/deploy.json to switch to a published port.");
            return 1;
        }

        if (host is null)
        {
            Console.Error.WriteLine("No host to deploy to. Pass --host user@box (it's remembered for next time).");
            Console.Error.WriteLine($"Usage: {Usage}");
            return 1;
        }

        // Resolve the project directory (the build context) and the app slug used for image/container names.
        var projectSetting = parsed.Option("project") ?? config.Project;
        var located = ProjectLocator.Locate(_fileSystem, _workingDirectory);
        var projectDir = ResolveProjectDirectory(projectSetting, located);
        var dockerfile = parsed.Option("dockerfile") ?? Path.Combine(projectDir, "Dockerfile");
        var contextDir = Path.GetDirectoryName(Path.GetFullPath(dockerfile)) ?? projectDir;
        var slug = ToContainerSlug(parsed.Option("name") ?? config.Name ?? located?.RootNamespace ?? new DirectoryInfo(projectDir).Name);

        if (!_fileSystem.FileExists(dockerfile))
        {
            Console.Error.WriteLine($"No Dockerfile found at '{dockerfile}'.");
            Console.Error.WriteLine("Scaffold one with `rask new <name> --docker`, or point at yours with --dockerfile <path>.");
            return 1;
        }

        // Gather runtime env from --env and an optional --env-file (KEY=VALUE lines; # comments allowed).
        if (!TryResolveEnv(parsed.MultiOption("env"), envFile, out var env, out var envError))
        {
            Console.Error.WriteLine(envError);
            return 1;
        }

        if (dryRun)
        {
            PrintPlan(host, slug, domain, port, dockerfile, contextDir, env);
            return 0;
        }

        // Preflight: local docker, then a single reachability probe that covers SSH auth + the remote daemon.
        if (!await DockerProbe.EnsureLocalAsync(_process, Console, cancellationToken).ConfigureAwait(false) ||
            !await DockerProbe.CanReachHostAsync(_process, Console, host, cancellationToken).ConfigureAwait(false))
        {
            return 1;
        }

        WriteHeading($"Building {slug}:latest on {host}…");
        if (await Run(BuildBuildArguments(host, slug, dockerfile, contextDir), cancellationToken).ConfigureAwait(false) != 0)
        {
            Console.WriteErrorLine("Docker build failed.", ConsoleStyle.Error);
            return 1;
        }

        return domain is null
            ? await DeployPortAsync(host, slug, port, env, projectSetting, envFile, cancellationToken).ConfigureAwait(false)
            : await DeployWithProxyAsync(host, slug, domain, env, projectSetting, envFile, cancellationToken).ConfigureAwait(false);
    }

    // ── The bare port path: stop-old-start-new (brief downtime — no proxy to swap behind). ──────────────
    private async Task<int> DeployPortAsync(string host, string slug, int port, IReadOnlyList<string> env, string? project, string? envFile, CancellationToken cancellationToken)
    {
        await Run(BuildRemoveArguments(host, slug), cancellationToken).ConfigureAwait(false); // ignore-absent
        Console.Out.WriteLine($"Starting {slug} on port {port}…");
        if (await Run(BuildRunArguments(host, slug, domain: null, color: null, port, env), cancellationToken).ConfigureAwait(false) != 0)
        {
            Console.WriteErrorLine("Failed to start the container.", ConsoleStyle.Error);
            return 1;
        }

        if (!await WaitUntilRunningAsync(host, slug, cancellationToken).ConfigureAwait(false))
        {
            await DumpLogsAsync(host, slug, cancellationToken).ConfigureAwait(false);
            return 1;
        }

        PersistConfig(host, domain: null, port, slug, project, envFile);
        Console.WriteLine($"Deployed. The app is live at http://{HostName(host)}:{port}", ConsoleStyle.Success);
        return 0;
    }

    // ── The domain path: blue-green swap behind a shared, multi-app Caddy proxy (zero downtime). ────────
    private async Task<int> DeployWithProxyAsync(
        string host, string slug, string domain, IReadOnlyList<string> env, string? project, string? envFile, CancellationToken cancellationToken)
    {
        // The live containers are the source of truth. Read them once: the current color of this app (to
        // pick the next), plus every other app's route (to regenerate the full Caddyfile).
        var apps = ParseDeployedApps((await Capture(BuildListArguments(host), cancellationToken).ConfigureAwait(false)).StandardOutput);
        var current = apps.FirstOrDefault(a => a.App == slug).Color;
        var newColor = NextColor(string.IsNullOrEmpty(current) ? null : current);
        var newContainer = $"{slug}-{newColor}";

        await Run(BuildNetworkCreateArguments(host, Network), cancellationToken).ConfigureAwait(false); // ignore-exists

        // Free the target-color name first: a prior deploy that failed after starting the new color (e.g. a
        // failed reload) can leave it behind, and `docker run --name` would otherwise collide.
        await Run(BuildRemoveArguments(host, newContainer), cancellationToken).ConfigureAwait(false); // ignore-absent

        Console.Out.WriteLine($"Starting {newContainer} ({domain})…");
        // In domain mode the app is reached internally on ContainerPort; no host port is published.
        if (await Run(BuildRunArguments(host, slug, domain, newColor, ContainerPort, env), cancellationToken).ConfigureAwait(false) != 0)
        {
            Console.WriteErrorLine("Failed to start the new container.", ConsoleStyle.Error);
            return 1;
        }

        // Gate before switching traffic: a container that never came up must not take the domain, and the
        // old one keeps serving (safe rollback — this deploy simply didn't happen).
        if (!await WaitUntilRunningAsync(host, newContainer, cancellationToken).ConfigureAwait(false))
        {
            await DumpLogsAsync(host, newContainer, cancellationToken).ConfigureAwait(false);
            await Run(BuildRemoveArguments(host, newContainer), cancellationToken).ConfigureAwait(false);
            Console.WriteErrorLine("The new container exited before it was ready — left the previous version serving.", ConsoleStyle.Error);
            return 1;
        }

        // Ensure the shared proxy is up (idempotent — an "already in use" name error is fine; we never
        // recreate it, so the rask-caddy-data volume keeps every app's ACME cert across deploys).
        await Run(BuildCaddyRunArguments(host, Network), cancellationToken).ConfigureAwait(false);

        // Regenerate the whole Caddyfile from the live routes, forcing this app to its NEW container, then
        // hot-reload — Caddy drains in-flight requests to the old color.
        var routes = BuildRoutingMap(apps, slug, domain, newContainer);
        var caddyfilePath = Path.Combine(Path.GetTempPath(), $"rask-{slug}.Caddyfile");
        _fileSystem.WriteAllText(caddyfilePath, BuildCaddyfile(routes));
        Console.Out.WriteLine($"Routing {domain} → {newContainer} (auto-HTTPS via Caddy)…");
        await Run(BuildCaddyCopyArguments(host, caddyfilePath), cancellationToken).ConfigureAwait(false);

        // Retry the reload: on a fresh host the proxy was just started detached, so its admin endpoint may
        // need a moment before `caddy reload` succeeds.
        if (await RunWithRetryAsync(BuildCaddyReloadArguments(host), cancellationToken).ConfigureAwait(false) != 0)
        {
            Console.WriteErrorLine("Caddy reload failed — the new container is running but not yet routed. Check `docker -H ssh://" + host + " logs rask-caddy`.", ConsoleStyle.Error);
            return 1;
        }

        // Traffic is on the new color: retire the old container(s) of this app.
        foreach (var stale in apps.Where(a => a.App == slug && a.Container != newContainer))
        {
            await Run(BuildRemoveArguments(host, stale.Container), cancellationToken).ConfigureAwait(false);
        }

        PersistConfig(host, domain, port: null, slug, project, envFile);
        Console.WriteLine($"Deployed. The app is live at https://{domain}", ConsoleStyle.Success);
        Console.WriteLine($"  (make sure {domain}'s DNS A/AAAA record points at {HostName(host)})", ConsoleStyle.Dim);
        return 0;
    }

    private void PersistConfig(string host, string? domain, int? port, string slug, string? project, string? envFile) =>
        new DeployConfig { Host = host, Domain = domain, Port = port, Name = slug, Project = project, EnvFile = envFile }
            .Save(_fileSystem, _workingDirectory);

    private async Task<bool> WaitUntilRunningAsync(string host, string container, CancellationToken cancellationToken)
    {
        // The poll is otherwise silent for up to ReadinessAttempts × ReadinessDelay — spin so an
        // interactive user sees it's working (a no-op when stdout is redirected/piped).
        await using var spinner = Spinner.Start(Console, $"Waiting for {container} to become healthy…");
        for (var attempt = 0; attempt < ReadinessAttempts; attempt++)
        {
            // Inspect first, then wait only between retries — a container that's already up returns immediately.
            if (attempt > 0 && ReadinessDelay > TimeSpan.Zero)
            {
                await Task.Delay(ReadinessDelay, cancellationToken).ConfigureAwait(false);
            }

            var result = await Capture(BuildInspectRunningArguments(host, container), cancellationToken).ConfigureAwait(false);
            if (result.ExitCode == 0 && result.StandardOutput.Trim().Equals("true", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    // Run a command, retrying on a non-zero exit (with the readiness delay between tries). Used for the
    // Caddy reload, whose admin endpoint may not be up yet the first time on a freshly-started proxy.
    private async Task<int> RunWithRetryAsync(IReadOnlyList<string> args, CancellationToken cancellationToken)
    {
        var exit = 0;
        for (var attempt = 0; attempt < ReadinessAttempts; attempt++)
        {
            if (attempt > 0 && ReadinessDelay > TimeSpan.Zero)
            {
                await Task.Delay(ReadinessDelay, cancellationToken).ConfigureAwait(false);
            }

            exit = await Run(args, cancellationToken).ConfigureAwait(false);
            if (exit == 0)
            {
                return 0;
            }
        }

        return exit;
    }

    private async Task DumpLogsAsync(string host, string container, CancellationToken cancellationToken)
    {
        var logs = await Capture(BuildLogsArguments(host, container), cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(logs.StandardOutput))
        {
            Console.Error.WriteLine(logs.StandardOutput.TrimEnd());
        }

        if (!string.IsNullOrWhiteSpace(logs.StandardError))
        {
            Console.Error.WriteLine(logs.StandardError.TrimEnd());
        }
    }

    private Task<int> Run(IReadOnlyList<string> args, CancellationToken cancellationToken) =>
        _process.RunAsync("docker", args, _workingDirectory, cancellationToken);

    private Task<ProcessResult> Capture(IReadOnlyList<string> args, CancellationToken cancellationToken) =>
        _process.CaptureAsync("docker", args, _workingDirectory, cancellationToken);

    private void PrintPlan(string host, string slug, string? domain, int port, string dockerfile, string contextDir, IReadOnlyList<string> env)
    {
        var writer = Console.Out;
        writer.WriteLine("Dry run — the following docker commands would run (no changes made):");
        writer.WriteLine();
        void Line(IReadOnlyList<string> args) => writer.WriteLine($"  docker {string.Join(' ', args)}");

        // Never echo secret values (e.g. from --env-file) to stdout — show the keys, hide the values.
        var redacted = RedactEnv(env);

        Line(BuildBuildArguments(host, slug, dockerfile, contextDir));
        if (domain is null)
        {
            Line(BuildRemoveArguments(host, slug));
            Line(BuildRunArguments(host, slug, domain: null, color: null, port, redacted));
            Line(BuildInspectRunningArguments(host, slug));
        }
        else
        {
            const string color = "blue"; // representative: the first color on a fresh host
            var container = $"{slug}-{color}";
            Line(BuildListArguments(host));
            Line(BuildNetworkCreateArguments(host, Network));
            Line(BuildRunArguments(host, slug, domain, color, port, redacted));
            Line(BuildInspectRunningArguments(host, container));
            Line(BuildCaddyRunArguments(host, Network));
            writer.WriteLine($"  # write a Caddyfile (regenerated from the host's live rask.* labels) and:");
            Line(BuildCaddyCopyArguments(host, $"<tmp>/rask-{slug}.Caddyfile"));
            Line(BuildCaddyReloadArguments(host));
            writer.WriteLine($"  # then remove the previous color's container");
        }
    }

    // ── Pure, deterministic helpers (unit-tested directly, like DbCommand.BuildEfArguments). ────────────

    /// <summary>The next blue-green color given the current one: nothing/green → blue, blue → green.</summary>
    internal static string NextColor(string? current) =>
        string.Equals(current, "blue", StringComparison.Ordinal) ? "green" : "blue";

    internal static IReadOnlyList<string> BuildBuildArguments(string host, string slug, string dockerfile, string contextDir) =>
        [.. Prefix(host), "build", "-t", $"{slug}:latest", "-f", dockerfile, contextDir];

    internal static IReadOnlyList<string> BuildNetworkCreateArguments(string host, string network) =>
        [.. Prefix(host), "network", "create", network];

    internal static IReadOnlyList<string> BuildRunArguments(string host, string slug, string? domain, string? color, int port, IReadOnlyList<string> env)
    {
        var args = new List<string>(Prefix(host)) { "run", "-d" };
        if (domain is null)
        {
            args.AddRange(["--name", slug, "--restart", "unless-stopped", "-p", $"{port}:{ContainerPort}"]);
        }
        else
        {
            args.AddRange(["--name", $"{slug}-{color}", "--restart", "unless-stopped", "--network", Network]);
            args.AddRange(["--label", "rask.managed=true", "--label", $"rask.app={slug}", "--label", $"rask.domain={domain}", "--label", $"rask.color={color}"]);
        }

        foreach (var entry in env)
        {
            args.AddRange(["-e", entry]);
        }

        args.Add($"{slug}:latest");
        return args;
    }

    internal static IReadOnlyList<string> BuildCaddyRunArguments(string host, string network) =>
    [
        .. Prefix(host), "run", "-d", "--name", CaddyContainer, "--restart", "unless-stopped",
        "--network", network, "-p", "80:80", "-p", "443:443", "-v", "rask-caddy-data:/data", "caddy:2",
    ];

    internal static IReadOnlyList<string> BuildListArguments(string host) =>
    [
        .. Prefix(host), "ps", "--filter", "label=rask.managed=true",
        "--format", "{{.Names}}\t{{.Label \"rask.app\"}}\t{{.Label \"rask.domain\"}}\t{{.Label \"rask.color\"}}",
    ];

    internal static IReadOnlyList<string> BuildInspectRunningArguments(string host, string container) =>
        [.. Prefix(host), "inspect", "--format", "{{.State.Running}}", container];

    internal static IReadOnlyList<string> BuildLogsArguments(string host, string container) =>
        [.. Prefix(host), "logs", "--tail", "50", container];

    internal static IReadOnlyList<string> BuildRemoveArguments(string host, string container) =>
        [.. Prefix(host), "rm", "-f", container];

    internal static IReadOnlyList<string> BuildCaddyCopyArguments(string host, string localCaddyfile) =>
        [.. Prefix(host), "cp", localCaddyfile, $"{CaddyContainer}:/etc/caddy/Caddyfile"];

    internal static IReadOnlyList<string> BuildCaddyReloadArguments(string host) =>
        [.. Prefix(host), "exec", CaddyContainer, "caddy", "reload", "--config", "/etc/caddy/Caddyfile", "--adapter", "caddyfile"];

    /// <summary>Parse the tab-separated <c>docker ps</c> label listing into deployed-app records.</summary>
    internal static IReadOnlyList<DeployedApp> ParseDeployedApps(string psOutput)
    {
        var apps = new List<DeployedApp>();
        foreach (var raw in psOutput.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length == 0)
            {
                continue;
            }

            var parts = line.Split('\t');
            if (parts.Length < 4 || parts[0].Length == 0)
            {
                continue;
            }

            apps.Add(new DeployedApp(parts[0], Label(parts[1]), Label(parts[2]), Label(parts[3])));
        }

        return apps;

        // docker prints "<no value>" for a label a container doesn't carry.
        static string Label(string value) => value == "<no value>" ? string.Empty : value;
    }

    /// <summary>
    /// The <c>domain → container</c> routing for the shared proxy: every other app keeps its live
    /// container; the app being deployed is forced to its new container. Sorted for a deterministic file.
    /// </summary>
    internal static IReadOnlyDictionary<string, string> BuildRoutingMap(
        IReadOnlyList<DeployedApp> apps, string deployingApp, string deployingDomain, string newContainer)
    {
        var map = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var app in apps)
        {
            if (app.Domain.Length == 0 || app.App == deployingApp)
            {
                continue; // port-mode apps aren't proxied; the deploying app is set explicitly below
            }

            map[app.Domain] = app.Container;
        }

        map[deployingDomain] = newContainer;
        return map;
    }

    /// <summary>Render the multi-site Caddyfile from a <c>domain → container</c> map (uses <c>\n</c> for determinism).</summary>
    internal static string BuildCaddyfile(IReadOnlyDictionary<string, string> routes)
    {
        var builder = new StringBuilder();
        foreach (var (domain, container) in routes)
        {
            if (builder.Length > 0)
            {
                builder.Append('\n');
            }

            builder.Append(domain).Append(" {\n");
            builder.Append("\treverse_proxy ").Append(container).Append(':').Append(ContainerPort).Append('\n');
            builder.Append("}\n");
        }

        return builder.ToString();
    }

    // Turn KEY=secret into KEY=… for display, so a dry-run preview never prints secret values.
    private static IReadOnlyList<string> RedactEnv(IReadOnlyList<string> env)
    {
        var redacted = new string[env.Count];
        for (var i = 0; i < env.Count; i++)
        {
            var eq = env[i].IndexOf('=', StringComparison.Ordinal);
            redacted[i] = eq >= 0 ? $"{env[i][..eq]}=…" : env[i];
        }

        return redacted;
    }

    private static string[] Prefix(string host) => ["-H", $"ssh://{host}"];

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    // The user's --host may be a bare "user@box", an "ssh://user@box" URL, or an ssh-config alias.
    // We store and compare the bare form; HostName strips the user@ for display/DNS hints.
    private static string HostName(string host)
    {
        var at = host.LastIndexOf('@');
        return at >= 0 ? host[(at + 1)..] : host;
    }

    private string ResolveProjectDirectory(string? projectOption, ProjectContext? located)
    {
        if (projectOption is not null)
        {
            var full = Path.GetFullPath(Path.Combine(_workingDirectory, projectOption));
            return projectOption.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
                ? Path.GetDirectoryName(full) ?? _workingDirectory
                : full;
        }

        // A single .csproj at/above the CWD gives the project dir; an ambiguous tree (e.g. wasm-hosted's
        // three projects) falls back to the CWD, where the solution-root Dockerfile lives.
        return located?.ProjectDirectory ?? _workingDirectory;
    }

    private static bool TryResolvePort(string? fromFlag, int? fromConfig, out int port, out string? error)
    {
        error = null;
        if (fromFlag is not null)
        {
            if (!int.TryParse(fromFlag, NumberStyles.Integer, CultureInfo.InvariantCulture, out port) || port is < 1 or > 65535)
            {
                error = $"--port must be a number between 1 and 65535, not '{fromFlag}'.";
                return false;
            }

            return true;
        }

        port = fromConfig ?? ContainerPort;
        return true;
    }

    private bool TryResolveEnv(IReadOnlyList<string> fromFlags, string? envFile, out IReadOnlyList<string> env, out string? error)
    {
        error = null;
        var entries = new List<string>();

        if (envFile is not null)
        {
            if (!_fileSystem.FileExists(envFile))
            {
                env = [];
                error = $"--env-file '{envFile}' doesn't exist.";
                return false;
            }

            foreach (var raw in _fileSystem.ReadAllText(envFile).Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith('#'))
                {
                    continue;
                }

                if (!line.Contains('=', StringComparison.Ordinal))
                {
                    env = [];
                    error = $"--env-file line isn't KEY=VALUE: '{line}'.";
                    return false;
                }

                entries.Add(line);
            }
        }

        foreach (var entry in fromFlags)
        {
            if (!entry.Contains('=', StringComparison.Ordinal))
            {
                env = [];
                error = $"--env must be KEY=VALUE, not '{entry}'.";
                return false;
            }

            entries.Add(entry);
        }

        env = entries;
        return true;
    }

    /// <summary>Lower-case a name into a Docker-safe image/container slug (<c>[a-z0-9._-]</c>).</summary>
    internal static string ToContainerSlug(string name)
    {
        var builder = new StringBuilder(name.Length);
        foreach (var c in name.ToLowerInvariant())
        {
            builder.Append(char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-' ? c : '-');
        }

        var slug = builder.ToString().Trim('-', '.', '_');
        return slug.Length == 0 ? "app" : slug;
    }
}

/// <summary>A rask-managed app container discovered from its <c>rask.*</c> labels via <c>docker ps</c>.</summary>
internal readonly record struct DeployedApp(string Container, string App, string Domain, string Color);
