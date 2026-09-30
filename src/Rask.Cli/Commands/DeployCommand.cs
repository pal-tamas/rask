using System.Globalization;
using Rask.Cli.Scaffolding;
using static Rask.Cli.Commands.DeployEnvironment;
using static Rask.Cli.Commands.DockerCommands;

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
        "rask deploy --env Rask__Mail__Smtp__Password=... --env-file .env.production",
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
            .WithJson()
            .Option("tail", valueHint: "n", description: "Log lines to show (logs only; default: 100, 'all' for everything).")
            // '-f' was --fields CLI-wide while `rask generate` existed, so this went without a short and
            // paid the `docker logs -f` muscle-memory cost. That command is gone and the letter is free,
            // so the tail of a log reads the way every other tool spells it. CliApplicationTests keeps
            // one meaning per short, which is what makes reclaiming a freed letter safe.
            .Flag("follow", 'f', description: "Stream new log lines until interrupted (logs only).")
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
        var config = DeployConfig.Load(_fileSystem, _workingDirectory, Console);
        var plan = new DeployPlan
        {
            Parsed = parsed,
            Config = config,
            Action = action,
            EnvFile = parsed.Option("env-file") ?? config.EnvFile,
            DryRun = parsed.HasFlag("dry-run"),
        };

        var failure = ResolvePorts(plan)
            ?? ResolveEndpoint(plan)
            ?? await ResolveProjectAsync(plan).ConfigureAwait(false)
            ?? await ResolveEnvAsync(plan).ConfigureAwait(false)
            ?? ResolveHealthAndSetup(plan);

        return failure ?? await RunPlanAsync(plan, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Everything a deploy resolved from its flags and <c>.rask/deploy.json</c>, before touching the host.</summary>
    private sealed class DeployPlan
    {
        public required ParsedArguments Parsed { get; init; }

        public required DeployConfig Config { get; init; }

        public string? Action { get; init; }

        public string? EnvFile { get; init; }

        public bool DryRun { get; init; }

        public string Host { get; set; } = string.Empty;

        public SshTarget SshTarget { get; set; }

        public string? Domain { get; set; }

        public int Port { get; set; }

        public int ContainerPort { get; set; }

        public string? ProjectSetting { get; set; }

        public string Dockerfile { get; set; } = string.Empty;

        public string ContextDir { get; set; } = string.Empty;

        public string Slug { get; set; } = string.Empty;

        public IReadOnlyList<string> Env { get; set; } = [];

        public bool HealthEnabled { get; set; }

        public string HealthPath { get; set; } = string.Empty;

        public SetupMode SetupMode { get; set; }

        public BootstrapOptions? BootstrapOptions { get; set; }

        /// <summary>The port to remember: none in domain mode, where no host port is published.</summary>
        public int? PublishedPort => Domain is null ? Port : null;
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
            HealthPath = string.Equals(healthPath, DefaultHealthPath, StringComparison.Ordinal) ? null : healthPath,
            HealthCheckDisabled = healthEnabled ? null : true,
            ContainerPort = containerPort == DefaultContainerPort ? null : containerPort,
            // Keys only — this file is committed. See DeployConfig.EnvKeys.
            EnvKeys = env is { Count: > 0 } ? EnvKeysOf(env) : null,
        }.Save(_fileSystem, _workingDirectory);

    private void PrintPlan(string host, string slug, string? domain, int port, int containerPort, string dockerfile, string contextDir, IReadOnlyList<string> env, bool healthEnabled, string healthPath)
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
            Line(BuildRunArguments(host, slug, domain: null, color: null, port, redacted, containerPort));
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
            Line(BuildRunArguments(host, slug, domain, color, containerPort, redacted, containerPort));
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

    /// <summary>
    /// Just the machine, for display, DNS hints and <c>ssh-keyscan</c> — no <c>user@</c>, and no
    /// <c>:port</c>. Delegates to <see cref="SshTarget"/> rather than re-deriving it: hand-stripping
    /// only the <c>user@</c> left the port attached, which silently produced a broken
    /// <c>ssh-keyscan box:2222</c> (→ an empty known_hosts secret → every CI deploy failing) and URLs
    /// like <c>http://box:2222:9000</c>.
    /// </summary>
    private static string HostName(string host) =>
        SshTarget.TryParse(host, out var target, out _) ? target.Host : host;
}
