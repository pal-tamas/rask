using System.Globalization;
using System.Text;
using Rask.Cli.Scaffolding;

namespace Rask.Cli.Commands;

/// <summary>
/// <c>rask deploy</c> — build the app's Docker image on a single host over SSH and run it, and (when a
/// <c>--domain</c> is given) front it with a shared Caddy reverse proxy that fetches an automatic
/// Let's Encrypt certificate. Every <em>deploy</em> operation is <c>docker -H ssh://user@host …</c>, so
/// there's no registry, no local daemon, and no image tarball — the build context ships to the box's
/// daemon and builds there.
///
/// <para>Host setup is the one exception, and it has to be: installing Docker over
/// <c>docker -H ssh://</c> is chicken-and-egg, so <see cref="HostSetup"/> shells out to plain
/// <c>ssh</c>. Handed a bare box, <c>rask deploy</c> installs Docker, creates a non-root deploy login,
/// configures a firewall and hardens SSH — so a fresh VPS reaches a live HTTPS app without the user
/// ever opening an SSH session themselves.</para>
///
/// <para>Multiple apps coexist on one box: each app container carries <c>rask.*</c> labels, so the box is
/// self-describing and the shared proxy's Caddyfile is regenerated from the live containers on every
/// deploy. The domain path is blue-green: the new container starts alongside the old and is waited on until
/// its container is <c>Running</c> and answers an HTTP health check, Caddy is reloaded to point at it, then
/// the old container is removed — so a container that fails to start, or that starts but fails its probe,
/// never takes traffic (the previous version keeps serving). The swap gates on <c>--health-path</c>
/// (default <c>/health</c>, the endpoint <c>rask new</c> scaffolds); <c>--no-health-check</c> falls back to
/// the container-running gate only.</para>
/// </summary>
internal sealed partial class DeployCommand(IConsole console, IFileSystem fileSystem, IProcessRunner process, string workingDirectory)
    : CliCommand(console)
{
    /// <summary>
    /// The port an app listens on <em>inside</em> its container, unless <c>--container-port</c> says otherwise.
    /// Every Dockerfile <c>rask new --docker</c> emits listens here, so the default is right for scaffolded
    /// apps; a hand-written Dockerfile that exposes something else needs the flag (it is then remembered).
    /// </summary>
    internal const int DefaultContainerPort = 8080;

    /// <summary>The shared docker network app containers and the Caddy proxy join in domain mode.</summary>
    internal const string Network = "rask";

    /// <summary>The shared reverse-proxy container name (one per host, routes every app's domain).</summary>
    internal const string CaddyContainer = "rask-caddy";

    /// <summary>A tiny, pinned curl image run as an ephemeral readiness probe joined to the target's netns.</summary>
    internal const string CurlImage = "curlimages/curl:8.11.1";

    /// <summary>The default path the HTTP readiness probe hits — the endpoint <c>rask new</c> scaffolds.</summary>
    internal const string DefaultHealthPath = "/health";

    /// <summary>Seconds a graceful <c>docker stop</c> waits after SIGTERM before SIGKILL. The top rung of
    /// <see cref="ShutdownBudget"/> — see there for the whole ladder and why each rung is the size it is.</summary>
    internal const int StopTimeoutSeconds = ShutdownBudget.DockerStopSeconds;

    /// <summary>
    /// The tag the running app is built to, and the one holding the version it replaced.
    ///
    /// <para>Deploys used to build straight to <c>:latest</c> every time, which left the previous image
    /// untagged and therefore unrecoverable — so a bad deploy that <em>passed</em> its health check (it
    /// starts and answers, it's just wrong) could only be undone by building again from fixed source.
    /// Keeping the last image under its own tag is what makes <c>rask deploy rollback</c> possible at
    /// all.</para>
    /// </summary>
    internal const string CurrentTag = "current";

    internal const string PreviousTag = "previous";

    private readonly IFileSystem _fileSystem = fileSystem;
    private readonly IProcessRunner _process = process;
    private readonly string _workingDirectory = workingDirectory;

    /// <summary>How long to wait between readiness polls, and how many — overridden to zero in tests.</summary>
    internal TimeSpan ReadinessDelay { get; set; } = TimeSpan.FromSeconds(2);

    internal int ReadinessAttempts { get; set; } = 10;

    /// <summary>How long to wait after the proxy has been pointed at the new color before stopping the old
    /// one — see <see cref="ShutdownBudget.PreStopDrainSeconds"/>. Overridden to zero in tests.</summary>
    internal TimeSpan PreStopDrainDelay { get; set; } = TimeSpan.FromSeconds(ShutdownBudget.PreStopDrainSeconds);

    public override string Name => "deploy";

    public override string Summary => "Build and deploy the app to a single host over SSH (auto-HTTPS with --domain).";

    public override IReadOnlyList<(string Name, string Description)> Arguments =>
    [
        ("[<action>]", "Operate on what's already deployed instead of deploying (omit to deploy)."),
    ];

    // The shape only — the actions and options are each listed once below, and --help renders both.
    public override string Usage => "rask deploy [<action>] [options]";

    public override IReadOnlyList<string> Examples =>
    [
        "rask deploy --host root@box.example.com --domain app.example.com",
        "rask deploy --host deploy@box.example.com --port 8080",
        "rask deploy --env ConnectionStrings__App=... --env-file .env.production",
        "rask deploy --github-actions",
        "rask deploy --dry-run",
        "rask deploy status",
        "rask deploy logs --follow",
        "rask deploy rollback",
    ];

    /// <summary>Options that only matter the first time a box is deployed to — grouped so --help stays readable.</summary>
    private const string SetupGroup = "Host setup options (first deploy to a box)";

    public override ArgumentSchema? OptionSchema => CreateSchema();

    private static ArgumentSchema CreateSchema() =>
        new ArgumentSchema()
            .Verb("status", "Show what is running, and on which color.")
            .Verb("logs", "Print the deployed app's logs.")
            .Verb("rollback", "Put the previous image back.")
            // No short name: '-h' is reserved for --help across the whole CLI, and a command that claimed
            // it would silently print help instead of running (see CliApplication.RequestsHelp).
            .Option("host", null, "user@box", "SSH target to build and run on (remembered in .rask/deploy.json).")
            .Option("domain", 'd', "host", "Public domain to serve over HTTPS via Caddy (implies ports 80/443).")
            .Option("port", valueHint: "n", description: "Published port when not using --domain (default: 8080).")
            .Option("container-port", valueHint: "n", description: "Port the app listens on inside the container (default: 8080; remembered).")
            .Option("project", 'p', "path", "Project to deploy (default: found from the current directory).")
            .Option("name", 'n', "slug", "Container/app name (default: derived from the project).")
            .Option("dockerfile", valueHint: "path", description: "Dockerfile to build (default: ./Dockerfile).")
            .Option("env-file", valueHint: "path", description: "File of KEY=VALUE lines to pass to the container.")
            .MultiOption("env", 'e', "KEY=VALUE", "Environment variable to pass (repeatable).")
            .Option("health-path", valueHint: "path", description: "HTTP path probed for readiness before the blue-green swap (default: /health).")
            .Flag("no-health-check", description: "Skip the post-deploy HTTP health check.")
            .Flag("github-actions", description: "Write a .github/workflows/deploy.yml that runs this deploy on push, and print the secrets to add.")
            .Flag("dry-run", description: "Print the docker commands that would run without changing anything.")
            .Option("tail", valueHint: "n", description: "Log lines to show (logs only; default: 100, 'all' for everything).")
            .Flag("follow", 'f', "Stream new log lines until interrupted (logs only).")
            .Flag("setup-host", group: SetupGroup, description: "Prepare the host without asking (installs Docker, creates the deploy user, firewall, SSH hardening).")
            .Flag("no-setup-host", group: SetupGroup, description: "Never change the host; fail with instructions if it isn't ready.")
            .Option("deploy-user", valueHint: "name", group: SetupGroup, description: "Non-root login to create and deploy as when given a root host (default: deploy).")
            .Flag("no-deploy-user", group: SetupGroup, description: "Keep deploying as the --host login instead of creating a non-root one.")
            .Flag("no-firewall", group: SetupGroup, description: "Don't configure ufw on the host.")
            .Flag("no-harden-ssh", group: SetupGroup, description: "Don't disable SSH password login and root login on the host.");

    public override async Task<int> ExecuteAsync(IReadOnlyList<string> args, CancellationToken cancellationToken)
    {
        var schema = CreateSchema();

        var parsed = schema.Parse(args);
        if (parsed.HasErrors)
        {
            return Fail(parsed.Errors);
        }

        // A bare `rask deploy` deploys; a verb after it operates on what is already deployed.
        if (parsed.Positionals.Count > 0 && !schema.TryResolveVerb(parsed.Positionals[0], out _))
        {
            return FailUnknownVerb(parsed.Positionals[0], schema);
        }

        if (parsed.Positionals.Count > 1)
        {
            return Fail($"Unexpected argument '{parsed.Positionals[1]}'.");
        }

        var action = parsed.Positionals.Count > 0 ? parsed.Positionals[0] : null;
        if (!TryRejectMisplacedOptions(parsed, action, out var optionError))
        {
            return Fail(optionError!);
        }

        // Flags win over the persisted config; anything unset falls back to .rask/deploy.json.
        var config = DeployConfig.Load(_fileSystem, _workingDirectory);
        var host = Normalize(parsed.Option("host") ?? config.Host);
        var domain = Normalize(parsed.Option("domain") ?? config.Domain);
        var envFile = parsed.Option("env-file") ?? config.EnvFile;
        var dryRun = parsed.HasFlag("dry-run");

        if (!TryResolvePort(parsed.Option("port"), config.Port, out var port, out var portError))
        {
            Console.WriteErrorLine(portError!, ConsoleStyle.Error);
            return 1;
        }

        if (!TryResolveContainerPort(parsed.Option("container-port"), config.ContainerPort, out var containerPort, out var containerPortError))
        {
            Console.WriteErrorLine(containerPortError!, ConsoleStyle.Error);
            return 1;
        }

        // Validated at the boundary for the same reason as the SSH host below: the domain is written
        // verbatim into the Caddyfile that fronts every app on the box, and it may come from the
        // *committed* .rask/deploy.json rather than from this user's keyboard.
        if (domain is not null)
        {
            if (!DomainName.TryParse(domain, out var parsedDomain, out var domainError))
            {
                Console.WriteErrorLine(domainError!, ConsoleStyle.Error);
                return 1;
            }

            domain = parsedDomain;
        }

        // --port and --domain are two different modes: with a domain the app is reached internally on the
        // proxy network, so a published host port is meaningless. Reject the combination explicitly rather
        // than silently ignore --port (which also covers a --port passed against a remembered domain).
        if (parsed.Option("port") is not null && domain is not null)
        {
            return Fail(parsed.Option("domain") is not null
                ? "--port doesn't apply with --domain (the app is served over HTTPS on 80/443 via the proxy)."
                : $"This app is deployed with --domain {domain} (remembered in .rask/deploy.json), so --port doesn't apply. Remove \"domain\" from .rask/deploy.json to switch to a published port.");
        }

        if (host is null)
        {
            return Fail("No host to deploy to. Pass --host user@box (it's remembered for next time).");
        }

        // Validated here, at the boundary, because the host reaches the `ssh` binary as an argument and
        // may come from the *committed* .rask/deploy.json — so it isn't necessarily this user's input.
        // A value like "-oProxyCommand=…" would otherwise run commands on whoever deploys the repo.
        if (!SshTarget.TryParse(host, out var sshTarget, out var hostError))
        {
            Console.WriteErrorLine(hostError!, ConsoleStyle.Error);
            return 1;
        }

        // Resolve the project directory (the build context) and the app slug used for image/container names.
        var projectSetting = parsed.Option("project") ?? config.Project;
        var located = ProjectLocator.Locate(_fileSystem, _workingDirectory);
        var projectDir = ResolveProjectDirectory(projectSetting, located);
        var dockerfile = parsed.Option("dockerfile") ?? Path.Combine(projectDir, "Dockerfile");
        var contextDir = Path.GetDirectoryName(Path.GetFullPath(dockerfile)) ?? projectDir;
        var slug = ToContainerSlug(parsed.Option("name") ?? config.Name ?? located?.RootNamespace ?? new DirectoryInfo(projectDir).Name);

        // Read off the project rather than remembered in deploy.json: the provider is a fact about the code
        // being deployed, so a stored copy could disagree with the app that is actually about to run.
        var provider = located?.Provider ?? DatabaseProvider.Sqlite;

        if (action is null && !_fileSystem.FileExists(dockerfile))
        {
            Console.WriteErrorLine($"No Dockerfile found at '{dockerfile}'.", ConsoleStyle.Error);
            Console.Error.WriteLine("Scaffold one with `rask new <name> --docker`, or point at yours with --dockerfile <path>.");
            return 1;
        }

        // Gather runtime env from --env and an optional --env-file (KEY=VALUE lines; # comments allowed).
        if (!TryResolveEnv(parsed.MultiOption("env"), envFile, out var env, out var envError))
        {
            Console.WriteErrorLine(envError!, ConsoleStyle.Error);
            return 1;
        }

        // A variable this app was deployed with last time, and isn't being given now, is almost never
        // intentional — it's a bare `rask deploy` after one that carried --env, or the generated CI
        // workflow, which passes none at all. Starting the app without it produces the worst kind of
        // failure: it boots, answers its health check, takes traffic, and is quietly misconfigured.
        if (action is null && MissingEnvKeys(config.EnvKeys, env) is { Count: > 0 } missing)
        {
            Console.WriteErrorLine(
                $"This app was last deployed with {string.Join(", ", missing)}, which {(missing.Count == 1 ? "isn't" : "aren't")} set now.",
                ConsoleStyle.Error);
            Console.Error.WriteLine();
            Console.Error.WriteLine("Deploying without it would start the app misconfigured, so this is a refusal rather than a warning.");
            Console.Error.WriteLine($"  • pass it again:      rask deploy {string.Join(' ', missing.Select(k => $"--env {k}=…"))}");
            Console.Error.WriteLine("  • or from a file:     rask deploy --env-file .env.production");
            Console.Error.WriteLine("  • deploying from CI?  add it to the deploy step in .github/workflows/deploy.yml");
            Console.Error.WriteLine($"  • no longer needed?   remove it from \"envKeys\" in {Path.Combine(".rask", "deploy.json")}");
            return 1;
        }

        // Readiness probe: once the container reports Running, confirm the app answers HTTP 2xx at the
        // health path before switching traffic. --no-health-check gates on Running only; --health-path
        // overrides the path (and re-enables a config-remembered disable). Both are remembered.
        if (parsed.HasFlag("no-health-check") && parsed.Option("health-path") is not null)
        {
            return Fail("--health-path doesn't apply with --no-health-check (the HTTP probe is disabled).");
        }

        var healthEnabled = !parsed.HasFlag("no-health-check")
            && (parsed.Option("health-path") is not null || !(config.HealthCheckDisabled ?? false));
        var healthPath = NormalizeHealthPath(parsed.Option("health-path") ?? config.HealthPath ?? DefaultHealthPath);

        if (!TryResolveSetup(parsed, out var setupMode, out var bootstrapOptions, out var setupError))
        {
            return Fail(setupError!);
        }

        // Pure scaffolding — never touches the host, so it works offline and before the box exists.
        if (parsed.HasFlag("github-actions"))
        {
            // The workflow reads host/domain/port from .rask/deploy.json, so it must exist before the
            // job ever runs. Writing it here means `rask deploy --github-actions` works as the FIRST
            // thing you do in a repo, rather than emitting a workflow that can't resolve a host.
            if (!dryRun)
            {
                PersistConfig(host, domain, domain is null ? port : null, slug, projectSetting, envFile, healthEnabled, healthPath, containerPort, env);
            }

            return WriteGitHubActionsWorkflow(sshTarget, host, dryRun);
        }

        // A client-server database is not on this box, so nothing can invent a connection string for it.
        // Checked before --dry-run returns: the point of a dry run is to find out the deploy won't work
        // *before* running it, and "you never told the app where its database is" qualifies.
        if (!DatabaseCatalog.For(provider).IsFileBased
            && !env.Any(e => e.StartsWith("ConnectionStrings__App=", StringComparison.Ordinal)))
        {
            Console.WriteErrorLine(
                $"This app uses {DatabaseCatalog.For(provider).ShortName}, so it needs a connection string.",
                ConsoleStyle.Error);
            Console.Error.WriteLine("    Pass it with:  rask deploy --env \"ConnectionStrings__App=...\"");
            Console.Error.WriteLine("    Or keep it out of your shell history with --env-file (see docs/deployment.md).");
            return 1;
        }

        if (dryRun)
        {
            PrintPlan(host, slug, domain, port, containerPort, dockerfile, contextDir, env, healthEnabled, healthPath, provider);
            return 0;
        }

        // Preflight: the local docker CLI is the client for every remote docker call, so it's required
        // even though nothing builds locally.
        if (!await DockerProbe.EnsureLocalAsync(_process, Console, cancellationToken).ConfigureAwait(false))
        {
            return 1;
        }

        // Probe the box and, if it isn't ready, offer to prepare it. This replaces the old
        // `docker -H ssh:// version` reachability check rather than adding to it — same one round-trip,
        // but it can tell "Docker isn't installed" from "you're not in the docker group".
        var setup = new HostSetup(Console, _process) { ReadinessDelay = ReadinessDelay, ReadinessAttempts = ReadinessAttempts };
        var ready = await setup.EnsureReadyAsync(sshTarget, bootstrapOptions with { PublishedPort = domain is null ? port : null }, setupMode, cancellationToken).ConfigureAwait(false);
        if (ready is null)
        {
            return 1;
        }

        // Setting up a bare box replaces the root login with a non-root one, so everything below —
        // and what we remember for next time — must use the new target, not what the user typed.
        var newHost = ready.Value.ToString();

        // Persisted the moment setup succeeds, NOT after a successful deploy. Host setup is
        // irreversible from the client's side: root SSH is now off, so `--host root@box` will never
        // work again. If we waited and the build failed (a broken Dockerfile — the likeliest outcome of
        // a first deploy), the new login would be lost and the user would be locked out of their own
        // box by a tool that had forgotten what it did to it.
        if (!string.Equals(newHost, host, StringComparison.Ordinal))
        {
            host = newHost;
            PersistConfig(host, domain, domain is null ? port : null, slug, projectSetting, envFile, healthEnabled, healthPath, containerPort, env);
            Console.WriteLine($"  Remembered {host} in {Path.Combine(".rask", "deploy.json")} — deploy as that from now on.", ConsoleStyle.Dim);
        }

        if (action is not null)
        {
            return action switch
            {
                "status" => await StatusAsync(host, slug, cancellationToken).ConfigureAwait(false),
                "logs" => await LogsAsync(host, slug, parsed, cancellationToken).ConfigureAwait(false),
                _ => await RollbackAsync(host, slug, domain, port, containerPort, env, healthEnabled, healthPath, cancellationToken).ConfigureAwait(false),
            };
        }

        // Move the live image aside before the build takes its tag, so the version being replaced stays
        // recoverable by `rask deploy rollback`. It fails harmlessly on a first deploy (no :current yet).
        await Run(BuildRetagArguments(host, slug, CurrentTag, PreviousTag), cancellationToken).ConfigureAwait(false); // ignore-absent

        // The deploy mounts a volume and points the app at a SQLite file on it, and the graceful-stop
        // budget below exists so a replicator can flush before the container dies. Say plainly when there
        // is no replicator: the database is then a single copy on one disk, and the "the box is
        // disposable" story the docs tell is not true of this deployment.
        //
        // None of that applies to a client-server database — it is not on this box, so there is no local
        // copy to warn about. (That its connection string is configured was already checked above.)
        if (DatabaseCatalog.For(provider).IsFileBased
            && !env.Any(e => e.StartsWith("Litestream__ReplicaUrl=", StringComparison.Ordinal)))
        {
            Console.WriteErrorLine(
                "  ! No Litestream replica configured — this app's database exists only on this box's disk.",
                ConsoleStyle.Warning);
            Console.Error.WriteLine("    Turn on continuous backup:  rask deploy --env \"Litestream__ReplicaUrl=s3://your-bucket/app\"  (see docs/sqlite.md)");
        }

        WriteHeading($"Building {slug}:{CurrentTag} on {host}…");
        if (await Run(BuildBuildArguments(host, slug, dockerfile, contextDir), cancellationToken).ConfigureAwait(false) != 0)
        {
            Console.WriteErrorLine("Docker build failed.", ConsoleStyle.Error);
            return 1;
        }

        return domain is null
            ? await DeployPortAsync(host, slug, port, containerPort, env, projectSetting, envFile, healthEnabled, healthPath, provider, cancellationToken).ConfigureAwait(false)
            : await DeployWithProxyAsync(host, slug, domain, containerPort, env, projectSetting, envFile, healthEnabled, healthPath, provider, cancellationToken).ConfigureAwait(false);
    }

    // ── The bare port path: stop-old-start-new (brief downtime — no proxy to swap behind). ──────────────
    private async Task<int> DeployPortAsync(string host, string slug, int port, int containerPort, IReadOnlyList<string> env, string? project, string? envFile, bool healthEnabled, string healthPath, DatabaseProvider provider, CancellationToken cancellationToken, string tag = CurrentTag, bool persist = true)
    {
        // Retire the old container gracefully (SIGTERM → WAL checkpoint, and a Litestream flush when a
        // replica is configured) before removing it, so the last writes are durable; both no-op on the
        // first deploy (no container yet).
        await Run(BuildStopArguments(host, slug), cancellationToken).ConfigureAwait(false); // ignore-absent
        await Run(BuildRemoveArguments(host, slug), cancellationToken).ConfigureAwait(false); // ignore-absent
        Console.Out.WriteLine($"Starting {slug} on port {port}…");
        var runtimeEnv = WriteEnvFile(slug, env);
        int started;
        try
        {
            started = await Run(BuildRunArguments(host, slug, domain: null, color: null, port, env, containerPort, tag, runtimeEnv, provider), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            // The values are inside the container now; the local copy has no reason to outlive the call.
            if (runtimeEnv is not null)
            {
                _fileSystem.TryDelete(runtimeEnv);
            }
        }

        if (started != 0)
        {
            Console.WriteErrorLine("Failed to start the container.", ConsoleStyle.Error);
            return 1;
        }

        if (!await WaitUntilRunningAsync(host, slug, cancellationToken).ConfigureAwait(false))
        {
            await DumpLogsAsync(host, slug, env, cancellationToken).ConfigureAwait(false);
            return await RestorePreviousAsync(
                host, slug, port, containerPort, env, healthEnabled, healthPath, provider, tag,
                "The new container did not stay running.", cancellationToken).ConfigureAwait(false);
        }

        // There is no blue-green swap on a single published port — the old container is stopped before the
        // new one starts, so the downtime is real and documented. What is NOT acceptable is *staying* down:
        // on a bad image the gate used to report the failure and leave nothing serving. It now re-enters
        // with `:previous`, which is the last image that passed this same gate.
        if (healthEnabled && !await WaitUntilHealthyAsync(host, slug, healthPath, containerPort, cancellationToken).ConfigureAwait(false))
        {
            await DumpLogsAsync(host, slug, env, cancellationToken).ConfigureAwait(false);
            return await RestorePreviousAsync(
                host, slug, port, containerPort, env, healthEnabled, healthPath, provider, tag,
                HealthFailureMessage(healthPath, rolledBack: false), cancellationToken).ConfigureAwait(false);
        }

        if (persist)
        {
            PersistConfig(host, domain: null, port, slug, project, envFile, healthEnabled, healthPath, containerPort, env);
        }
        Console.WriteLine($"Deployed. The app is live at http://{HostName(host)}:{port}", ConsoleStyle.Success);
        return 0;
    }

    /// <summary>
    ///     Port mode's automatic rollback: bring <c>:previous</c> back after a deploy that started but did
    ///     not come up healthy. Always returns a non-zero exit code — the deploy failed either way; the
    ///     only question is whether the box is left serving something.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Guarded by <paramref name="tag" />: this only fires for a deploy of <c>:current</c>, so the
    ///         re-entry (which passes <see cref="PreviousTag" />) cannot recurse. That condition is the
    ///         whole recursion guard — there is no depth counter to get wrong.
    ///     </para>
    ///     <para>
    ///         Tags are deliberately left alone, unlike <c>rask deploy rollback</c>, which swaps them.
    ///         Nothing about the *configuration* succeeded here: the operator fixes the image and deploys
    ///         again, which overwrites <c>:current</c>. Swapping would file the last known-good image away
    ///         as <c>:previous</c> and let the next deploy lose it.
    ///     </para>
    /// </remarks>
    private async Task<int> RestorePreviousAsync(
        string host, string slug, int port, int containerPort, IReadOnlyList<string> env,
        bool healthEnabled, string healthPath, DatabaseProvider provider, string tag, string reason,
        CancellationToken cancellationToken)
    {
        if (tag != CurrentTag)
        {
            // Already the restore attempt. Report plainly rather than trying again.
            Console.WriteErrorLine($"{reason} The previous version did not come up either.", ConsoleStyle.Error);
            return 1;
        }

        if (await ResolveRollbackImageAsync(host, slug, cancellationToken).ConfigureAwait(false) is null)
        {
            Console.WriteErrorLine($"{reason} There is no previous image to fall back to.", ConsoleStyle.Error);
            Console.Error.WriteLine($"{slug}:{PreviousTag} is written by the deploy that replaces it, so the first deploy of an app has no predecessor.");
            return 1;
        }

        Console.WriteErrorLine(reason, ConsoleStyle.Error);
        WriteHeading($"Restoring {slug}:{PreviousTag}…");

        var restored = await DeployPortAsync(
            host, slug, port, containerPort, env, project: null, envFile: null, healthEnabled, healthPath,
            provider, cancellationToken, tag: PreviousTag, persist: false).ConfigureAwait(false);

        if (restored == 0)
        {
            Console.WriteLine(
                $"The previous version is serving again on port {port}. The deploy itself failed — fix the image and deploy again.",
                ConsoleStyle.Warning);
        }

        return 1;
    }

    // ── The domain path: blue-green swap behind a shared, multi-app Caddy proxy (zero downtime). ────────
    private async Task<int> DeployWithProxyAsync(
        string host, string slug, string domain, int containerPort, IReadOnlyList<string> env, string? project, string? envFile, bool healthEnabled, string healthPath, DatabaseProvider provider, CancellationToken cancellationToken, string tag = CurrentTag, bool persist = true)
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
        // In domain mode the app is reached internally on its container port; no host port is published.
        var runtimeEnv = WriteEnvFile(slug, env);
        int started;
        try
        {
            started = await Run(BuildRunArguments(host, slug, domain, newColor, containerPort, env, containerPort, tag, runtimeEnv, provider), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (runtimeEnv is not null)
            {
                _fileSystem.TryDelete(runtimeEnv);
            }
        }

        if (started != 0)
        {
            Console.WriteErrorLine("Failed to start the new container.", ConsoleStyle.Error);
            return 1;
        }

        // Gate before switching traffic: a container that never came up must not take the domain, and the
        // old one keeps serving (safe rollback — this deploy simply didn't happen).
        if (!await WaitUntilRunningAsync(host, newContainer, cancellationToken).ConfigureAwait(false))
        {
            await DumpLogsAsync(host, newContainer, env, cancellationToken).ConfigureAwait(false);
            await Run(BuildRemoveArguments(host, newContainer), cancellationToken).ConfigureAwait(false);
            Console.WriteErrorLine("The new container exited before it was ready — left the previous version serving.", ConsoleStyle.Error);
            return 1;
        }

        // Second gate: the container is up, but is the app actually answering? Probe over HTTP before
        // touching the proxy, so a container that starts but 500s (bad config, failed migration) never
        // takes the domain — remove it and leave the old color serving (safe rollback).
        if (healthEnabled && !await WaitUntilHealthyAsync(host, newContainer, healthPath, containerPort, cancellationToken).ConfigureAwait(false))
        {
            await DumpLogsAsync(host, newContainer, env, cancellationToken).ConfigureAwait(false);
            await Run(BuildRemoveArguments(host, newContainer), cancellationToken).ConfigureAwait(false);
            Console.Error.WriteLine(HealthFailureMessage(healthPath, rolledBack: true));
            return 1;
        }

        // Ensure the shared proxy is up (idempotent — an "already in use" name error is fine; we never
        // recreate it, so the rask-caddy-data volume keeps every app's ACME cert across deploys).
        await Run(BuildCaddyRunArguments(host, Network), cancellationToken).ConfigureAwait(false);

        // Regenerate the whole Caddyfile from the live routes, forcing this app to its NEW container, then
        // hot-reload — Caddy drains in-flight requests to the old color.
        var routes = BuildRoutingMap(apps, slug, domain, new RouteTarget(newContainer, containerPort));
        var caddyfilePath = Path.Combine(Path.GetTempPath(), $"rask-{slug}.Caddyfile");
        _fileSystem.WriteAllText(caddyfilePath, BuildCaddyfile(routes));
        Console.Out.WriteLine($"Routing {domain} → {newContainer} (auto-HTTPS via Caddy)…");
        await Run(BuildCaddyCopyArguments(host, caddyfilePath), cancellationToken).ConfigureAwait(false);

        // The file has been copied to the box; leaving a predictably-named copy in the shared temp dir
        // serves no purpose and is a symlink-clobber target on a multi-user machine.
        _fileSystem.TryDelete(caddyfilePath);

        // Retry the reload: on a fresh host the proxy was just started detached, so its admin endpoint may
        // need a moment before `caddy reload` succeeds.
        if (await RunWithRetryAsync(BuildCaddyReloadArguments(host), cancellationToken).ConfigureAwait(false) != 0)
        {
            Console.WriteErrorLine("Caddy reload failed — the new container is running but not yet routed. Check `docker -H ssh://" + host + " logs rask-caddy`.", ConsoleStyle.Error);
            return 1;
        }

        // Let the proxy finish with the old color before pulling it out from under itself. `caddy reload`
        // returns as soon as the admin API applies the config, but Caddy still holds pooled keep-alive
        // connections to the old upstream — a request it is about to write onto one of those when SIGTERM
        // lands gets a broken connection, and with the default lb_try_duration of 0 it is not retried, i.e.
        // a 502 to a real user. There used to be no gap here at all.
        //
        // Deliberately NOT sized for live sessions: a WebSocket to the old color survives until the app
        // closes it, so the SIGTERM is what triggers the client's move to the new container. Draining those
        // gracefully is the app's job (RaskServerOptions.ShutdownDrainTimeout), not this pause's.
        if (PreStopDrainDelay > TimeSpan.Zero)
        {
            await Task.Delay(PreStopDrainDelay, cancellationToken).ConfigureAwait(false);
        }

        // Traffic is on the new color: retire the old container(s) of this app. Graceful stop first so
        // SQLite checkpoints the WAL — and, when a replica is configured, the Litestream replicator flushes
        // — before removal. A plain rm -f (SIGKILL) would drop the last frames.
        foreach (var stale in apps.Where(a => a.App == slug && a.Container != newContainer))
        {
            await Run(BuildStopArguments(host, stale.Container), cancellationToken).ConfigureAwait(false);
            await Run(BuildRemoveArguments(host, stale.Container), cancellationToken).ConfigureAwait(false);
        }

        if (persist)
        {
            PersistConfig(host, domain, port: null, slug, project, envFile, healthEnabled, healthPath, containerPort, env);
        }
        Console.WriteLine($"Deployed. The app is live at https://{domain}", ConsoleStyle.Success);
        Console.WriteLine($"  (make sure {domain}'s DNS A/AAAA record points at {HostName(host)})", ConsoleStyle.Dim);
        return 0;
    }

    /// <summary>
    /// Write <c>.github/workflows/deploy.yml</c> and print the two secrets it needs. Everything the
    /// workflow varies on already lives in <c>.rask/deploy.json</c>, so the file itself is fixed and
    /// this needs no network — it works before the host exists.
    /// </summary>
    private int WriteGitHubActionsWorkflow(SshTarget target, string host, bool dryRun)
    {
        var path = Path.Combine(_workingDirectory, GitHubActionsWorkflow.RelativePath);

        // The parsed host: no user@, no :port. ssh-keyscan takes the port as -p, so leaving it on the
        // name would scan nothing and hand CI an empty known_hosts secret.
        var hostName = target.Host;
        var keyscan = target.Port is { } p
            ? $"ssh-keyscan -p {p.ToString(CultureInfo.InvariantCulture)} {hostName}"
            : $"ssh-keyscan {hostName}";

        if (dryRun)
        {
            Console.Out.WriteLine($"Dry run — would write {GitHubActionsWorkflow.RelativePath}:");
            Console.Out.WriteLine();
            Console.Out.WriteLine(GitHubActionsWorkflow.Content);
            return 0;
        }

        // A workflow is a thing people edit. Overwriting one silently would throw away their changes.
        if (_fileSystem.FileExists(path))
        {
            Console.WriteErrorLine($"{GitHubActionsWorkflow.RelativePath} already exists — leaving it alone.", ConsoleStyle.Error);
            Console.Error.WriteLine("Delete it first if you want a fresh one, or edit it in place.");
            return 1;
        }

        _fileSystem.CreateDirectory(Path.GetDirectoryName(path)!);
        _fileSystem.WriteAllText(path, GitHubActionsWorkflow.Content);
        WriteCreated(GitHubActionsWorkflow.RelativePath);

        Console.Out.WriteLine();
        WriteHeading("Add these two repository secrets, then push to main:");
        Console.Out.WriteLine();
        Console.WriteLine($"  gh secret set {GitHubActionsWorkflow.KeySecret} < ~/.ssh/id_ed25519", ConsoleStyle.Code);
        Console.WriteLine($"  gh secret set {GitHubActionsWorkflow.KnownHostsSecret} --body \"$({keyscan} 2>/dev/null)\"", ConsoleStyle.Code);
        Console.Out.WriteLine();
        Console.WriteLine($"  (use the private key that already logs in to {host} — the deploy runs as that user)", ConsoleStyle.Dim);
        Console.WriteLine($"  (the workflow deploys with --no-setup-host: prepare the box once with `rask deploy --setup-host`)", ConsoleStyle.Dim);
        return 0;
    }

    /// <summary>
    /// Remembered keys that aren't being supplied this time. Ordinal + sorted so the message is stable.
    /// </summary>
    internal static IReadOnlyList<string> MissingEnvKeys(IReadOnlyList<string>? remembered, IReadOnlyList<string> supplied)
    {
        if (remembered is null || remembered.Count == 0)
        {
            return [];
        }

        var have = EnvKeysOf(supplied).ToHashSet(StringComparer.Ordinal);
        return [.. remembered.Where(k => !have.Contains(k)).Order(StringComparer.Ordinal)];
    }

    /// <summary>The KEY halves of a set of KEY=VALUE entries, de-duplicated and sorted.</summary>
    internal static string[] EnvKeysOf(IReadOnlyList<string> env) =>
    [
        .. env
            .Select(e => e.IndexOf('=', StringComparison.Ordinal) is var i and >= 0 ? e[..i] : e)
            .Where(k => k.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal),
    ];

    private void PersistConfig(string host, string? domain, int? port, string slug, string? project, string? envFile, bool healthEnabled, string healthPath, int containerPort, IReadOnlyList<string>? env = null) =>
        new DeployConfig
        {
            Host = host,
            Domain = domain,
            Port = port,
            Name = slug,
            Project = project,
            EnvFile = envFile,
            // Only persist non-defaults so a fresh deploy.json stays clean (default path, health on).
            HealthPath = healthPath == DefaultHealthPath ? null : healthPath,
            HealthCheckDisabled = healthEnabled ? null : true,
            ContainerPort = containerPort == DefaultContainerPort ? null : containerPort,
            // Keys only — this file is committed. See DeployConfig.EnvKeys.
            EnvKeys = env is { Count: > 0 } ? EnvKeysOf(env) : null,
        }.Save(_fileSystem, _workingDirectory);

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

    // Probe the app over HTTP from an ephemeral curl container sharing the target's network namespace, so
    // it works whether or not a host port is published (domain mode publishes none) and needs no HTTP client
    // in the app image. Retried across the readiness window — the app may warm up after the container is up.
    private async Task<bool> WaitUntilHealthyAsync(string host, string container, string healthPath, int containerPort, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < ReadinessAttempts; attempt++)
        {
            // Probe first, then wait only between retries — a warm app passes on the first attempt.
            if (attempt > 0 && ReadinessDelay > TimeSpan.Zero)
            {
                await Task.Delay(ReadinessDelay, cancellationToken).ConfigureAwait(false);
            }

            if (await Run(BuildHealthCheckArguments(host, container, healthPath, containerPort), cancellationToken).ConfigureAwait(false) == 0)
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

    /// <summary>
    /// Show the failing container's last log lines, with any value we passed in via <c>--env</c> /
    /// <c>--env-file</c> masked out first. An app that logs its own configuration on a failed start is
    /// ordinary, and this output goes to stderr — which, in the workflow <c>--github-actions</c> writes,
    /// is a CI job log. We can't know what else is a secret, but we do know exactly what we handed it.
    /// </summary>
    private async Task DumpLogsAsync(string host, string container, IReadOnlyList<string> env, CancellationToken cancellationToken)
    {
        var logs = await Capture(BuildLogsArguments(host, container), cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(logs.StandardOutput))
        {
            Console.Error.WriteLine(MaskSecrets(logs.StandardOutput.TrimEnd(), env));
        }

        if (!string.IsNullOrWhiteSpace(logs.StandardError))
        {
            Console.Error.WriteLine(MaskSecrets(logs.StandardError.TrimEnd(), env));
        }
    }

    /// <summary>
    /// An env file is line-oriented, so a value containing a newline can't round-trip through one.
    /// Those entries keep going in as <c>-e</c> rather than being silently truncated.
    /// </summary>
    internal static bool CanGoInEnvFile(string entry) =>
        entry.IndexOf('=', StringComparison.Ordinal) > 0 && !entry.Contains('\n') && !entry.Contains('\r');

    /// <summary>
    /// Write the runtime environment to a local file for <c>--env-file</c>, or return null when there is
    /// nothing it can carry. The file is this machine's, not the host's: the docker CLI reads it here and
    /// sends the values over the API, which is the point — they never reach an argv anyone can read.
    /// </summary>
    private string? WriteEnvFile(string slug, IReadOnlyList<string> env)
    {
        var carriable = env.Where(CanGoInEnvFile).ToArray();
        if (carriable.Length == 0)
        {
            return null;
        }

        var path = Path.Combine(Path.GetTempPath(), $"rask-{slug}-{Guid.NewGuid():N}.env");
        _fileSystem.WriteAllText(path, string.Join('\n', carriable) + "\n");
        return path;
    }

    /// <summary>Replace every non-trivial <c>--env</c> value found in <paramref name="text"/> with an ellipsis.</summary>
    internal static string MaskSecrets(string text, IReadOnlyList<string> env)
    {
        foreach (var entry in env)
        {
            var eq = entry.IndexOf('=', StringComparison.Ordinal);
            if (eq < 0)
            {
                continue;
            }

            // Very short values ("1", "true", a port) are not secrets and masking them would shred the
            // log into ellipses, destroying the diagnostics this dump exists to provide.
            var value = entry[(eq + 1)..];
            if (value.Length >= 6)
            {
                text = text.Replace(value, "…", StringComparison.Ordinal);
            }
        }

        return text;
    }

    private Task<int> Run(IReadOnlyList<string> args, CancellationToken cancellationToken) =>
        _process.RunAsync("docker", args, _workingDirectory, cancellationToken);

    private Task<ProcessResult> Capture(IReadOnlyList<string> args, CancellationToken cancellationToken) =>
        _process.CaptureAsync("docker", args, _workingDirectory, cancellationToken);

    private void PrintPlan(string host, string slug, string? domain, int port, int containerPort, string dockerfile, string contextDir, IReadOnlyList<string> env, bool healthEnabled, string healthPath, DatabaseProvider provider)
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
            Line(BuildRunArguments(host, slug, domain: null, color: null, port, redacted, containerPort, provider: provider));
            Line(BuildInspectRunningArguments(host, slug));
            if (healthEnabled)
            {
                Line(BuildHealthCheckArguments(host, slug, healthPath, containerPort));
            }
        }
        else
        {
            const string color = "blue"; // representative: the first color on a fresh host
            var container = $"{slug}-{color}";
            Line(BuildListArguments(host));
            Line(BuildNetworkCreateArguments(host, Network));
            Line(BuildRunArguments(host, slug, domain, color, containerPort, redacted, containerPort, provider: provider));
            Line(BuildInspectRunningArguments(host, container));
            if (healthEnabled)
            {
                Line(BuildHealthCheckArguments(host, container, healthPath, containerPort));
            }

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

    /// <summary>
    /// Build the image. Tagged <c>:current</c> (what containers are started from, and what the next deploy
    /// will move aside to <c>:previous</c>) and <c>:latest</c>, which is kept purely so the box still reads
    /// the way a person expects when they run <c>docker images</c> themselves.
    /// </summary>
    internal static IReadOnlyList<string> BuildBuildArguments(string host, string slug, string dockerfile, string contextDir) =>
        [.. Prefix(host), "build", "-t", $"{slug}:{CurrentTag}", "-t", $"{slug}:latest", "-f", dockerfile, contextDir];

    /// <summary>Move the live image aside before a build overwrites its tag, so it can be rolled back to.</summary>
    internal static IReadOnlyList<string> BuildRetagArguments(string host, string slug, string from, string to) =>
        [.. Prefix(host), "tag", $"{slug}:{from}", $"{slug}:{to}"];

    /// <summary>Does this tag exist on the host? Used to tell "nothing to roll back to" from a failure.</summary>
    internal static IReadOnlyList<string> BuildImageExistsArguments(string host, string slug, string tag) =>
        [.. Prefix(host), "image", "inspect", "--format", "{{.Id}}", $"{slug}:{tag}"];

    internal static IReadOnlyList<string> BuildNetworkCreateArguments(string host, string network) =>
        [.. Prefix(host), "network", "create", network];

    /// <summary>
    /// The <c>docker run</c> for an app container. Runtime environment goes in through
    /// <paramref name="envFilePath"/> when there is one: <c>--env-file</c> is read by the local docker CLI,
    /// so the values never appear in this machine's process table the way <c>-e KEY=VALUE</c> does. Entries
    /// whose value spans lines can't be expressed in that format and stay inline.
    /// </summary>
    internal static IReadOnlyList<string> BuildRunArguments(string host, string slug, string? domain, string? color, int port, IReadOnlyList<string> env, int containerPort = DefaultContainerPort, string tag = CurrentTag, string? envFilePath = null, DatabaseProvider provider = DatabaseProvider.Sqlite)
    {
        var args = new List<string>(Prefix(host)) { "run", "-d" };

        // Container-runtime hygiene for a box that is expected to run unattended for months:
        //  • json-file logs are unbounded by default, and a chatty app filling the disk takes the whole
        //    host down with it — including every other app sharing the box.
        //  • no-new-privileges stops a compromised process gaining rights via setuid binaries. It costs
        //    nothing here: nothing a Rask app does needs to escalate.
        args.AddRange(["--log-opt", "max-size=10m", "--log-opt", "max-file=3", "--security-opt", "no-new-privileges"]);
        if (domain is null)
        {
            args.AddRange(["--name", slug, "--restart", "unless-stopped", "-p", $"{port}:{containerPort}"]);

            // Labelled like a domain-mode container (minus a domain, so BuildRoutingMap skips it): without
            // this a port-mode deploy is invisible to the host inventory, so switching an app to --domain
            // would strand its old container running forever.
            args.AddRange(["--label", "rask.managed=true", "--label", $"rask.app={slug}", "--label", $"rask.port={containerPort.ToString(CultureInfo.InvariantCulture)}"]);
        }
        else
        {
            args.AddRange(["--name", $"{slug}-{color}", "--restart", "unless-stopped", "--network", Network]);
            args.AddRange(["--label", "rask.managed=true", "--label", $"rask.app={slug}", "--label", $"rask.domain={domain}", "--label", $"rask.color={color}"]);
            args.AddRange(["--label", $"rask.port={containerPort.ToString(CultureInfo.InvariantCulture)}"]);
        }

        // Persist the SQLite database on a per-app named volume so it survives container replacement — every
        // deploy runs a fresh container, and without this the DB (in the container's writable layer) would be
        // destroyed on every redeploy. Point the app at it via ConnectionStrings:App, which Rask-scaffolded
        // apps honour; the volume is shared by both blue/green colors so the swap keeps the same database. A
        // user-supplied --env / --env-file value is appended after, so an explicit override still wins.
        // ASPNETCORE_ENVIRONMENT is what selects appsettings.Production.json and turns off the developer
        // exception page; relying on the base image's default left a deployed app in whatever environment
        // the image happened to assume. Set before the user's own --env, so an explicit override still wins.
        args.AddRange(["-e", "ASPNETCORE_ENVIRONMENT=Production"]);

        // Only a file database gets a volume and a connection string invented for it. On a client-server
        // database there is nothing on this box to persist, and guessing a connection string would be worse
        // than having none: the app would start against the wrong database instead of failing. The deploy
        // path refuses that case up front unless the user supplied ConnectionStrings__App themselves.
        if (DatabaseCatalog.For(provider).IsFileBased)
        {
            args.AddRange(["-v", $"{slug}-data:/data", "-e", "ConnectionStrings__App=Data Source=/data/app.db"]);
        }

        // The log store keeps a file of its own, so it needs its own pointer onto the same volume — without
        // this it would land in the container's writable layer and be destroyed by the very restart it
        // exists to survive. Harmless on an app that doesn't use Rask.Logging: nothing reads the value.
        args.AddRange(["-e", "ConnectionStrings__Logs=Data Source=/data/logs.db"]);

        if (envFilePath is not null)
        {
            args.AddRange(["--env-file", envFilePath]);
        }

        foreach (var entry in env)
        {
            // With an env file, only the entries it cannot carry are still passed inline.
            if (envFilePath is null || !CanGoInEnvFile(entry))
            {
                args.AddRange(["-e", entry]);
            }
        }

        args.Add($"{slug}:{tag}");
        return args;
    }

    /// <summary>
    /// Gracefully stop a container: SIGTERM, then SIGKILL after <see cref="StopTimeoutSeconds"/>. Used before
    /// retiring a container that's serving, so SQLite checkpoints the WAL cleanly before exit — and, when a
    /// replica is configured, the in-process Litestream replicator flushes. A plain <c>rm -f</c> (SIGKILL)
    /// would lose the last frames.
    /// </summary>
    internal static IReadOnlyList<string> BuildStopArguments(string host, string container) =>
        [.. Prefix(host), "stop", "-t", StopTimeoutSeconds.ToString(CultureInfo.InvariantCulture), container];

    internal static IReadOnlyList<string> BuildCaddyRunArguments(string host, string network) =>
    [
        .. Prefix(host), "run", "-d", "--name", CaddyContainer, "--restart", "unless-stopped",
        "--network", network, "-p", "80:80", "-p", "443:443", "-v", "rask-caddy-data:/data", "caddy:2",
    ];

    internal static IReadOnlyList<string> BuildListArguments(string host) =>
    [
        .. Prefix(host), "ps", "--filter", "label=rask.managed=true",
        "--format", "{{.Names}}\t{{.Label \"rask.app\"}}\t{{.Label \"rask.domain\"}}\t{{.Label \"rask.color\"}}\t{{.Label \"rask.port\"}}",
    ];

    internal static IReadOnlyList<string> BuildInspectRunningArguments(string host, string container) =>
        [.. Prefix(host), "inspect", "--format", "{{.State.Running}}", container];

    /// <summary>
    /// An ephemeral <c>curl</c> container joined to <paramref name="container"/>'s network namespace, hitting
    /// the app on <c>localhost:8080</c>. <c>--rm</c> cleans it up; <c>-f</c> makes a <c>&gt;= 400</c> response a
    /// non-zero exit; <c>-m 5</c> bounds a hung connect. Exit 0 ⇒ the app answered a success status.
    /// </summary>
    internal static IReadOnlyList<string> BuildHealthCheckArguments(string host, string container, string healthPath, int containerPort = DefaultContainerPort) =>
    [
        .. Prefix(host), "run", "--rm", "--network", $"container:{container}", CurlImage,
        "-fsS", "-m", "5", $"http://localhost:{containerPort}{healthPath}",
    ];

    internal static IReadOnlyList<string> BuildLogsArguments(string host, string container, string tail = "50", bool follow = false)
    {
        var args = new List<string>(Prefix(host)) { "logs", "--tail", tail };
        if (follow)
        {
            args.Add("--follow");
        }

        args.Add(container);
        return args;
    }

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

            // A container deployed before rask.port existed reports no label; it can only have been
            // listening on the then-hardcoded default, so that is the correct reading of its absence.
            var port = parts.Length > 4 && int.TryParse(Label(parts[4]), NumberStyles.None, CultureInfo.InvariantCulture, out var labelled)
                ? labelled
                : DeployCommand.DefaultContainerPort;

            apps.Add(new DeployedApp(parts[0], Label(parts[1]), Label(parts[2]), Label(parts[3]), port));
        }

        return apps;

        // docker prints "<no value>" for a label a container doesn't carry.
        static string Label(string value) => value == "<no value>" ? string.Empty : value;
    }

    /// <summary>
    /// The <c>domain → container</c> routing for the shared proxy: every other app keeps its live
    /// container; the app being deployed is forced to its new container. Sorted for a deterministic file.
    /// </summary>
    internal static IReadOnlyDictionary<string, RouteTarget> BuildRoutingMap(
        IReadOnlyList<DeployedApp> apps, string deployingApp, string deployingDomain, RouteTarget newTarget)
    {
        var map = new SortedDictionary<string, RouteTarget>(StringComparer.Ordinal);
        foreach (var app in apps)
        {
            if (app.Domain.Length == 0 || app.App == deployingApp)
            {
                continue; // port-mode apps aren't proxied; the deploying app is set explicitly below
            }

            // Each app keeps ITS OWN container port: one box can host apps that don't agree on one.
            map[app.Domain] = new RouteTarget(app.Container, app.Port);
        }

        map[deployingDomain] = newTarget;
        return map;
    }

    /// <summary>Render the multi-site Caddyfile from a <c>domain → container</c> map (uses <c>\n</c> for determinism).</summary>
    internal static string BuildCaddyfile(IReadOnlyDictionary<string, RouteTarget> routes)
    {
        var builder = new StringBuilder();
        foreach (var (domain, target) in routes)
        {
            if (builder.Length > 0)
            {
                builder.Append('\n');
            }

            // `domain` reached here through DomainName.TryParse, so it cannot close this block early.
            builder.Append(domain).Append(" {\n");
            builder.Append("\treverse_proxy ").Append(target.Container).Append(':').Append(target.Port).Append('\n');
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

    // A health path is a URL path — tolerate "health" and store "/health" so the probe URL is well-formed.
    private static string NormalizeHealthPath(string path)
    {
        var trimmed = path.Trim();
        return trimmed.StartsWith('/') ? trimmed : "/" + trimmed;
    }

    private static string HealthFailureMessage(string healthPath, bool rolledBack) =>
        $"The new container is running but failed its HTTP health check at {healthPath}"
        + (rolledBack ? " — left the previous version serving." : ".")
        + " Fix the app, or deploy with --no-health-check (or --health-path <path> if readiness is served elsewhere).";

    /// <summary>
    /// Just the machine, for display, DNS hints and <c>ssh-keyscan</c> — no <c>user@</c>, and no
    /// <c>:port</c>. Delegates to <see cref="SshTarget"/> rather than re-deriving it: hand-stripping
    /// only the <c>user@</c> left the port attached, which silently produced a broken
    /// <c>ssh-keyscan box:2222</c> (→ an empty known_hosts secret → every CI deploy failing) and URLs
    /// like <c>http://box:2222:9000</c>.
    /// </summary>
    private static string HostName(string host) =>
        SshTarget.TryParse(host, out var target, out _) ? target.Host : host;

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

    /// <summary>
    /// Merge the host-setup flags into a mode and a set of options. Contradictions are rejected rather
    /// than silently resolved — <c>--setup-host --no-setup-host</c> means the user is confused about
    /// something that changes a production box, and guessing is the wrong answer.
    /// </summary>
    internal static bool TryResolveSetup(ParsedArguments parsed, out SetupMode mode, out BootstrapOptions options, out string? error)
    {
        mode = SetupMode.Ask;
        options = new BootstrapOptions(BootstrapOptions.DefaultDeployUser, Firewall: true, HardenSsh: true, PublishedPort: null);
        error = null;

        var forced = parsed.HasFlag("setup-host");
        var disabled = parsed.HasFlag("no-setup-host");
        if (forced && disabled)
        {
            error = "--setup-host and --no-setup-host contradict each other.";
            return false;
        }

        var deployUser = parsed.Option("deploy-user");
        if (parsed.HasFlag("no-deploy-user") && deployUser is not null)
        {
            error = "--deploy-user doesn't apply with --no-deploy-user.";
            return false;
        }

        if (deployUser is not null && !HostBootstrap.IsValidUserName(deployUser))
        {
            // Rejected before we ever connect — this name would otherwise reach a remote shell.
            error = $"--deploy-user '{deployUser}' isn't a valid Linux user name (lower-case letters, digits, '_' and '-', not starting with a digit).";
            return false;
        }

        mode = forced ? SetupMode.Forced : disabled ? SetupMode.Disabled : SetupMode.Ask;
        options = new BootstrapOptions(
            DeployUser: parsed.HasFlag("no-deploy-user") ? null : deployUser ?? BootstrapOptions.DefaultDeployUser,
            Firewall: !parsed.HasFlag("no-firewall"),
            HardenSsh: !parsed.HasFlag("no-harden-ssh"),
            PublishedPort: null);
        return true;
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

        port = fromConfig ?? DefaultContainerPort;
        return true;
    }

    /// <summary>
    /// The port the app listens on inside its container. Unlike <c>--port</c> this isn't a host-side
    /// publish: it's what the proxy is pointed at and what the readiness probe hits, so getting it wrong
    /// means a deploy that builds and starts and then never answers.
    /// </summary>
    private static bool TryResolveContainerPort(string? fromFlag, int? fromConfig, out int containerPort, out string? error)
    {
        error = null;
        if (fromFlag is not null)
        {
            if (!int.TryParse(fromFlag, NumberStyles.Integer, CultureInfo.InvariantCulture, out containerPort) || containerPort is < 1 or > 65535)
            {
                error = $"--container-port must be a number between 1 and 65535, not '{fromFlag}'.";
                return false;
            }

            return true;
        }

        containerPort = fromConfig ?? DefaultContainerPort;
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

            var lineNumber = 0;
            foreach (var raw in _fileSystem.ReadAllText(envFile).Split('\n'))
            {
                lineNumber++;
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith('#'))
                {
                    continue;
                }

                if (!line.Contains('=', StringComparison.Ordinal))
                {
                    env = [];

                    // The LINE NUMBER, never the line: an env file holds secrets, and the offending line
                    // is the one we've decided we can't parse — echoing it would print a credential to
                    // stderr (and into a CI log) precisely when something has gone wrong.
                    error = $"--env-file '{envFile}' line {lineNumber.ToString(CultureInfo.InvariantCulture)} isn't KEY=VALUE.";
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
/// <summary>
/// A live Rask-managed container, as read back from its own labels. <see cref="Port"/> is what the app
/// listens on inside the container — carried as a label so the proxy config can be regenerated for a
/// host running several apps that don't all listen on the same port.
/// </summary>
internal readonly record struct DeployedApp(string Container, string App, string Domain, string Color, int Port);

/// <summary>Where the shared proxy sends one domain: a container and the port it listens on.</summary>
internal readonly record struct RouteTarget(string Container, int Port);
