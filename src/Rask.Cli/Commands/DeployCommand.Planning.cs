using System.Globalization;
using System.Text;
using Rask.Cli.Scaffolding;
using static Rask.Cli.Commands.DeployEnvironment;

namespace Rask.Cli.Commands;

internal sealed partial class DeployCommand
{
    private int? ResolvePorts(DeployPlan plan)
    {
        if (!TryResolvePort(plan.Parsed.Option("port"), plan.Config.Port, out var port, out var portError))
        {
            Console.WriteErrorLine(portError!, ConsoleStyle.Error);
            return 1;
        }

        if (!TryResolveContainerPort(plan.Parsed.Option("container-port"), plan.Config.ContainerPort, out var containerPort, out var containerPortError))
        {
            Console.WriteErrorLine(containerPortError!, ConsoleStyle.Error);
            return 1;
        }

        plan.Port = port;
        plan.ContainerPort = containerPort;
        return null;
    }

    private int? ResolveEndpoint(DeployPlan plan)
    {
        var parsed = plan.Parsed;
        var host = Normalize(parsed.Option("host") ?? plan.Config.Host);
        var domain = Normalize(parsed.Option("domain") ?? plan.Config.Domain);

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

        plan.Host = host;
        plan.Domain = domain;
        plan.SshTarget = sshTarget;
        return null;
    }

    private async Task<int?> ResolveProjectAsync(DeployPlan plan)
    {
        // Resolve the project directory (the build context) and the app slug used for image/container names.
        var parsed = plan.Parsed;
        plan.ProjectSetting = parsed.Option("project") ?? plan.Config.Project;
        var located = ProjectLocator.Locate(_fileSystem, _workingDirectory);
        var projectDir = ResolveProjectDirectory(plan.ProjectSetting, located);
        plan.Dockerfile = parsed.Option("dockerfile") ?? Path.Combine(projectDir, "Dockerfile");
        plan.ContextDir = Path.GetDirectoryName(Path.GetFullPath(plan.Dockerfile)) ?? projectDir;
        plan.Slug = ToContainerSlug(parsed.Option("name") ?? plan.Config.Name ?? located?.RootNamespace ?? new DirectoryInfo(projectDir).Name);

        if (plan.Action is null && !_fileSystem.FileExists(plan.Dockerfile))
        {
            Console.WriteErrorLine($"No Dockerfile found at '{plan.Dockerfile}'.", ConsoleStyle.Error);
            await Console.Error.WriteLineAsync("Scaffold one with `rask new <name> --docker`, or point at yours with --dockerfile <path>.").ConfigureAwait(false);
            return 1;
        }

        return null;
    }

    private async Task<int?> ResolveEnvAsync(DeployPlan plan)
    {
        // Gather runtime env from --env and an optional --env-file (KEY=VALUE lines; # comments allowed).
        if (!TryResolveEnv(plan.Parsed.MultiOption("env"), plan.EnvFile, out var env, out var envError))
        {
            Console.WriteErrorLine(envError!, ConsoleStyle.Error);
            return 1;
        }

        plan.Env = env;

        // A variable this app was deployed with last time, and isn't being given now, is almost never
        // intentional — it's a bare `rask deploy` after one that carried --env, or the generated CI
        // workflow, which passes none at all. Starting the app without it produces the worst kind of
        // failure: it boots, answers its health check, takes traffic, and is quietly misconfigured.
        if (plan.Action is null && MissingEnvKeys(plan.Config.EnvKeys, env) is { Count: > 0 } missing)
        {
            Console.WriteErrorLine(
                $"This app was last deployed with {string.Join(", ", missing)}, which {(missing.Count == 1 ? "isn't" : "aren't")} set now.",
                ConsoleStyle.Error);
            await Console.Error.WriteLineAsync().ConfigureAwait(false);
            await Console.Error.WriteLineAsync("Deploying without it would start the app misconfigured, so this is a refusal rather than a warning.").ConfigureAwait(false);
            await Console.Error.WriteLineAsync($"  • pass it again:      rask deploy {string.Join(' ', missing.Select(k => $"--env {k}=…"))}").ConfigureAwait(false);
            await Console.Error.WriteLineAsync("  • or from a file:     rask deploy --env-file .env.production").ConfigureAwait(false);
            await Console.Error.WriteLineAsync("  • deploying from CI?  add it to the deploy step in .github/workflows/deploy.yml").ConfigureAwait(false);
            await Console.Error.WriteLineAsync($"  • no longer needed?   remove it from \"envKeys\" in {Path.Combine(".rask", "deploy.json")}").ConfigureAwait(false);
            return 1;
        }

        return null;
    }

    private int? ResolveHealthAndSetup(DeployPlan plan)
    {
        var parsed = plan.Parsed;

        // Readiness probe: once the container reports Running, confirm the app answers HTTP 2xx at the
        // health path before switching traffic. --no-health-check gates on Running only; --health-path
        // overrides the path (and re-enables a config-remembered disable). Both are remembered.
        if (parsed.HasFlag("no-health-check") && parsed.Option("health-path") is not null)
        {
            return Fail("--health-path doesn't apply with --no-health-check (the HTTP probe is disabled).");
        }

        plan.HealthEnabled = !parsed.HasFlag("no-health-check")
            && (parsed.Option("health-path") is not null || !(plan.Config.HealthCheckDisabled ?? false));
        plan.HealthPath = NormalizeHealthPath(parsed.Option("health-path") ?? plan.Config.HealthPath ?? DefaultHealthPath);

        if (!TryResolveSetup(parsed, out var setupMode, out var bootstrapOptions, out var setupError))
        {
            return Fail(setupError!);
        }

        plan.SetupMode = setupMode;
        plan.BootstrapOptions = bootstrapOptions;
        return null;
    }

    private void PersistPlan(DeployPlan plan) =>
        PersistConfig(plan.Host, plan.Domain, plan.PublishedPort, plan.Slug, plan.ProjectSetting, plan.EnvFile, plan.HealthEnabled, plan.HealthPath, plan.ContainerPort, plan.Env);

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

        mode = (forced, disabled) switch
        {
            (true, _) => SetupMode.Forced,
            (_, true) => SetupMode.Disabled,
            _ => SetupMode.Ask,
        };
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

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    // A health path is a URL path — tolerate "health" and store "/health" so the probe URL is well-formed.
    private static string NormalizeHealthPath(string path)
    {
        var trimmed = path.Trim();
        return trimmed.StartsWith('/') ? trimmed : "/" + trimmed;
    }
}
