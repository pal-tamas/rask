using System.Globalization;
using static Rask.Cli.Commands.CaddyRouting;
using static Rask.Cli.Commands.DeployEnvironment;
using static Rask.Cli.Commands.DockerCommands;

namespace Rask.Cli.Commands;

internal sealed partial class DeployCommand
{
    private async Task<int> RunPlanAsync(DeployPlan plan, CancellationToken cancellationToken)
    {
        // Pure scaffolding — never touches the host, so it works offline and before the box exists.
        if (plan.Parsed.HasFlag("github-actions"))
        {
            // The workflow reads host/domain/port from .rask/deploy.json, so it must exist before the
            // job ever runs. Writing it here means `rask deploy --github-actions` works as the FIRST
            // thing you do in a repo, rather than emitting a workflow that can't resolve a host.
            if (!plan.DryRun)
            {
                PersistPlan(plan);
            }

            return WriteGitHubActionsWorkflow(plan.SshTarget, plan.Host, plan.DryRun);
        }

        if (plan.DryRun)
        {
            PrintPlan(plan.Host, plan.Slug, plan.Domain, plan.Port, plan.ContainerPort, plan.Dockerfile, plan.ContextDir, plan.Env, plan.HealthEnabled, plan.HealthPath);
            return 0;
        }

        if (!await PrepareHostAsync(plan, cancellationToken).ConfigureAwait(false))
        {
            return 1;
        }

        return plan.Action switch
        {
            null => await BuildAndDeployAsync(plan, cancellationToken).ConfigureAwait(false),
            "status" => await StatusAsync(plan.Host, plan.Slug, plan.Parsed.HasFlag("json"), cancellationToken).ConfigureAwait(false),
            "logs" => await LogsAsync(plan.Host, plan.Slug, plan.Parsed, cancellationToken).ConfigureAwait(false),
            _ => await RollbackAsync(plan.Host, plan.Slug, plan.Domain, plan.Port, plan.ContainerPort, plan.Env, plan.HealthEnabled, plan.HealthPath, cancellationToken).ConfigureAwait(false),
        };
    }

    /// <summary>Checks the local docker CLI and the box, preparing the box when it isn't ready.</summary>
    private async Task<bool> PrepareHostAsync(DeployPlan plan, CancellationToken cancellationToken)
    {
        // Preflight: the local docker CLI is the client for every remote docker call, so it's required
        // even though nothing builds locally.
        if (!await DockerProbe.EnsureLocalAsync(_process, Console, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        // Probe the box and, if it isn't ready, offer to prepare it. This replaces the old
        // `docker -H ssh:// version` reachability check rather than adding to it — same one round-trip,
        // but it can tell "Docker isn't installed" from "you're not in the docker group".
        var setup = new HostSetup(Console, _process) { ReadinessDelay = ReadinessDelay, ReadinessAttempts = ReadinessAttempts };
        // ContainerPort rides along because the firewall has to allow the port *inside* the container:
        // Docker's DNAT rewrites the destination before any filter rule sees it. In domain mode nothing
        // but Caddy publishes a port, so there's none to pass.
        var ready = await setup.EnsureReadyAsync(
            plan.SshTarget,
            plan.BootstrapOptions! with { PublishedPort = plan.PublishedPort, ContainerPort = plan.Domain is null ? plan.ContainerPort : null },
            plan.SetupMode,
            cancellationToken).ConfigureAwait(false);
        if (ready is null)
        {
            return false;
        }

        // Setting up a bare box replaces the root login with a non-root one, so everything below —
        // and what we remember for next time — must use the new target, not what the user typed.
        var newHost = ready.Value.ToString();

        // Persisted the moment setup succeeds, NOT after a successful deploy. Host setup is
        // irreversible from the client's side: root SSH is now off, so `--host root@box` will never
        // work again. If we waited and the build failed (a broken Dockerfile — the likeliest outcome of
        // a first deploy), the new login would be lost and the user would be locked out of their own
        // box by a tool that had forgotten what it did to it.
        if (!string.Equals(newHost, plan.Host, StringComparison.Ordinal))
        {
            plan.Host = newHost;
            PersistPlan(plan);
            Console.WriteLine($"  Remembered {plan.Host} in {Path.Combine(".rask", "deploy.json")} — deploy as that from now on.", ConsoleStyle.Dim);
        }

        return true;
    }

    private async Task<int> BuildAndDeployAsync(DeployPlan plan, CancellationToken cancellationToken)
    {
        var (host, slug, env) = (plan.Host, plan.Slug, plan.Env);

        // Move the live image aside before the build takes its tag, so the version being replaced stays
        // recoverable by `rask deploy rollback`. It fails harmlessly on a first deploy (no :current yet).
        await Run(BuildRetagArguments(host, slug, CurrentTag, PreviousTag), cancellationToken).ConfigureAwait(false); // ignore-absent

        // The deploy mounts a volume and points the app at a SQLite file on it, and the graceful-stop
        // budget below exists so a replicator can flush before the container dies. Say plainly when there
        // is no replicator: the database is then a single copy on one disk, and the "the box is
        // disposable" story the docs tell is not true of this deployment.
        if (!env.Any(e => e.StartsWith("Rask__Litestream__ReplicaUrl=", StringComparison.Ordinal)))
        {
            Console.WriteErrorLine(
                "  ! No Litestream replica configured — this app's database exists only on this box's disk.",
                ConsoleStyle.Warning);
            await Console.Error.WriteLineAsync("    Turn on continuous backup:  rask deploy --env \"Rask__Litestream__ReplicaUrl=s3://your-bucket/app\"  (see docs/sqlite.md)").ConfigureAwait(false);
        }

        // With --domain the deploy names the public origin itself. On a bare port it cannot know the address people
        // use (an ssh alias is not one), and the app will not guess it from a request, so emailed links stay off.
        if (plan.Domain is null && !env.Any(e => e.StartsWith("Rask__Auth__PublicOrigin=", StringComparison.Ordinal)))
        {
            Console.WriteErrorLine(
                "  ! No public address set — confirm and reset emails will not be sent until there is one.",
                ConsoleStyle.Warning);
            await Console.Error.WriteLineAsync("    Name it:  rask deploy --env \"Rask__Auth__PublicOrigin=http://your-host:" + plan.Port.ToString(CultureInfo.InvariantCulture) + "\"  (or deploy with --domain)").ConfigureAwait(false);
        }

        WriteHeading($"Building {slug}:{CurrentTag} on {host}…");
        if (await Run(BuildBuildArguments(host, slug, plan.Dockerfile, plan.ContextDir), cancellationToken).ConfigureAwait(false) != 0)
        {
            Console.WriteErrorLine("Docker build failed — its output is above. Run `docker build .` locally to reproduce it, then deploy again.", ConsoleStyle.Error);
            return 1;
        }

        return plan.Domain is null
            ? await DeployPortAsync(host, slug, plan.Port, plan.ContainerPort, env, plan.ProjectSetting, plan.EnvFile, plan.HealthEnabled, plan.HealthPath, cancellationToken).ConfigureAwait(false)
            : await DeployWithProxyAsync(host, slug, plan.Domain, plan.ContainerPort, env, plan.ProjectSetting, plan.EnvFile, plan.HealthEnabled, plan.HealthPath, cancellationToken).ConfigureAwait(false);
    }

    // ── The bare port path: stop-old-start-new (brief downtime — no proxy to swap behind). ──────────────
    private async Task<int> DeployPortAsync(string host, string slug, int port, int containerPort, IReadOnlyList<string> env, string? project, string? envFile, bool healthEnabled, string healthPath, CancellationToken cancellationToken, string tag = CurrentTag, bool persist = true)
    {
        // Retire the old container gracefully (SIGTERM → WAL checkpoint, and a Litestream flush when a
        // replica is configured) before removing it, so the last writes are durable; both no-op on the
        // first deploy (no container yet).
        await Run(BuildStopArguments(host, slug), cancellationToken).ConfigureAwait(false); // ignore-absent
        await Run(BuildRemoveArguments(host, slug), cancellationToken).ConfigureAwait(false); // ignore-absent
        await Console.Out.WriteLineAsync($"Starting {slug} on port {port}…").ConfigureAwait(false);
        var runtimeEnv = WriteEnvFile(slug, env);
        int started;
        try
        {
            started = await Run(BuildRunArguments(host, slug, domain: null, color: null, port, env, containerPort, tag, runtimeEnv), cancellationToken).ConfigureAwait(false);
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
                host, slug, port, containerPort, env, healthEnabled, healthPath, tag,
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
                host, slug, port, containerPort, env, healthEnabled, healthPath, tag,
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
        bool healthEnabled, string healthPath, string tag, string reason,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(tag, CurrentTag, StringComparison.Ordinal))
        {
            // Already the restore attempt. Report plainly rather than trying again.
            Console.WriteErrorLine($"{reason} The previous version did not come up either.", ConsoleStyle.Error);
            return 1;
        }

        if (await ResolveRollbackImageAsync(host, slug, cancellationToken).ConfigureAwait(false) is null)
        {
            Console.WriteErrorLine($"{reason} There is no previous image to fall back to.", ConsoleStyle.Error);
            await Console.Error.WriteLineAsync("This is the app's first deploy, so there is no earlier version to go back to. Fix the problem above and deploy again.").ConfigureAwait(false);
            return 1;
        }

        Console.WriteErrorLine(reason, ConsoleStyle.Error);
        WriteHeading($"Restoring {slug}:{PreviousTag}…");

        var restored = await DeployPortAsync(
            host, slug, port, containerPort, env, project: null, envFile: null, healthEnabled, healthPath,
            cancellationToken, tag: PreviousTag, persist: false).ConfigureAwait(false);

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
        string host, string slug, string domain, int containerPort, IReadOnlyList<string> env, string? project, string? envFile, bool healthEnabled, string healthPath, CancellationToken cancellationToken, string tag = CurrentTag, bool persist = true)
    {
        // The live containers are the source of truth. Read them once: the current color of this app (to
        // pick the next), plus every other app's route (to regenerate the full Caddyfile).
        var apps = ParseDeployedApps((await Capture(BuildListArguments(host), cancellationToken).ConfigureAwait(false)).StandardOutput);
        var current = apps.FirstOrDefault(a => string.Equals(a.App, slug, StringComparison.Ordinal)).Color;
        var newColor = NextColor(string.IsNullOrEmpty(current) ? null : current);
        var newContainer = $"{slug}-{newColor}";

        if (!await StartColorAsync(host, slug, domain, newColor, containerPort, env, tag, cancellationToken).ConfigureAwait(false)
            || !await GateColorAsync(host, newContainer, containerPort, env, healthEnabled, healthPath, cancellationToken).ConfigureAwait(false)
            || !await RouteToColorAsync(host, slug, domain, apps, new RouteTarget(newContainer, containerPort), cancellationToken).ConfigureAwait(false))
        {
            return 1;
        }

        await RetireOldColorsAsync(host, slug, apps, newContainer, cancellationToken).ConfigureAwait(false);

        if (persist)
        {
            PersistConfig(host, domain, port: null, slug, project, envFile, healthEnabled, healthPath, containerPort, env);
        }
        Console.WriteLine($"Deployed. The app is live at https://{domain}", ConsoleStyle.Success);
        Console.WriteLine($"  (make sure {domain}'s DNS A/AAAA record points at {HostName(host)})", ConsoleStyle.Dim);
        return 0;
    }

    /// <summary>Starts the next color's container beside the live one.</summary>
    private async Task<bool> StartColorAsync(
        string host, string slug, string domain, string newColor, int containerPort, IReadOnlyList<string> env, string tag, CancellationToken cancellationToken)
    {
        var newContainer = $"{slug}-{newColor}";
        await Run(BuildNetworkCreateArguments(host, Network), cancellationToken).ConfigureAwait(false); // ignore-exists

        // Free the target-color name first: a prior deploy that failed after starting the new color (e.g. a
        // failed reload) can leave it behind, and `docker run --name` would otherwise collide.
        await Run(BuildRemoveArguments(host, newContainer), cancellationToken).ConfigureAwait(false); // ignore-absent

        await Console.Out.WriteLineAsync($"Starting {newContainer} ({domain})…").ConfigureAwait(false);
        // In domain mode the app is reached internally on its container port; no host port is published.
        var runtimeEnv = WriteEnvFile(slug, env);
        int started;
        try
        {
            started = await Run(BuildRunArguments(host, slug, domain, newColor, containerPort, env, containerPort, tag, runtimeEnv), cancellationToken).ConfigureAwait(false);
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
            return false;
        }

        return true;
    }

    /// <summary>Whether the new color came up and answers; when it didn't, it is removed and the old one keeps serving.</summary>
    private async Task<bool> GateColorAsync(
        string host, string newContainer, int containerPort, IReadOnlyList<string> env, bool healthEnabled, string healthPath, CancellationToken cancellationToken)
    {
        // Gate before switching traffic: a container that never came up must not take the domain, and the
        // old one keeps serving (safe rollback — this deploy simply didn't happen).
        if (!await WaitUntilRunningAsync(host, newContainer, cancellationToken).ConfigureAwait(false))
        {
            await DumpLogsAsync(host, newContainer, env, cancellationToken).ConfigureAwait(false);
            await Run(BuildRemoveArguments(host, newContainer), cancellationToken).ConfigureAwait(false);
            Console.WriteErrorLine("The new container exited before it was ready — left the previous version serving.", ConsoleStyle.Error);
            return false;
        }

        // Second gate: the container is up, but is the app actually answering? Probe over HTTP before
        // touching the proxy, so a container that starts but 500s (bad config, failed migration) never
        // takes the domain — remove it and leave the old color serving (safe rollback).
        if (healthEnabled && !await WaitUntilHealthyAsync(host, newContainer, healthPath, containerPort, cancellationToken).ConfigureAwait(false))
        {
            await DumpLogsAsync(host, newContainer, env, cancellationToken).ConfigureAwait(false);
            await Run(BuildRemoveArguments(host, newContainer), cancellationToken).ConfigureAwait(false);
            await Console.Error.WriteLineAsync(HealthFailureMessage(healthPath, rolledBack: true)).ConfigureAwait(false);
            return false;
        }

        return true;
    }

    /// <summary>Points the shared proxy at the new color.</summary>
    private async Task<bool> RouteToColorAsync(
        string host, string slug, string domain, IReadOnlyList<DeployedApp> apps, RouteTarget target, CancellationToken cancellationToken)
    {
        // Ensure the shared proxy is up (idempotent — an "already in use" name error is fine; we never
        // recreate it, so the rask-caddy-data volume keeps every app's ACME cert across deploys).
        await Run(BuildCaddyRunArguments(host, Network), cancellationToken).ConfigureAwait(false);

        // Regenerate the whole Caddyfile from the live routes, forcing this app to its NEW container, then
        // hot-reload — Caddy drains in-flight requests to the old color.
        var routes = BuildRoutingMap(apps, slug, domain, target);
        // Unguessable and owner-only: on a shared machine a predictable name in the temp dir is one another user can
        // create first, and whatever it held would be copied into the proxy that fronts every app on the box.
        var caddyfilePath = Path.Combine(Path.GetTempPath(), $"rask-{slug}-{Guid.NewGuid():N}.Caddyfile");
        _fileSystem.WriteSecretText(caddyfilePath, BuildCaddyfile(routes));
        await Console.Out.WriteLineAsync($"Routing {domain} → {target.Container} (auto-HTTPS via Caddy)…").ConfigureAwait(false);
        await Run(BuildCaddyCopyArguments(host, caddyfilePath), cancellationToken).ConfigureAwait(false);

        // The file has been copied to the box; leaving a predictably-named copy in the shared temp dir
        // serves no purpose and is a symlink-clobber target on a multi-user machine.
        _fileSystem.TryDelete(caddyfilePath);

        // Retry the reload: on a fresh host the proxy was just started detached, so its admin endpoint may
        // need a moment before `caddy reload` succeeds.
        if (await RunWithRetryAsync(BuildCaddyReloadArguments(host), cancellationToken).ConfigureAwait(false) != 0)
        {
            Console.WriteErrorLine("Caddy reload failed — the new container is running but not yet routed. Check `docker -H ssh://" + host + " logs rask-caddy`.", ConsoleStyle.Error);
            return false;
        }

        return true;
    }

    /// <summary>Stops and removes this app's other colors once traffic is on the new one.</summary>
    private async Task RetireOldColorsAsync(
        string host, string slug, IReadOnlyList<DeployedApp> apps, string newContainer, CancellationToken cancellationToken)
    {
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
        var staleContainers = apps
            .Where(a => string.Equals(a.App, slug, StringComparison.Ordinal))
            .Select(a => a.Container)
            .Where(container => !string.Equals(container, newContainer, StringComparison.Ordinal));
        foreach (var stale in staleContainers)
        {
            await Run(BuildStopArguments(host, stale), cancellationToken).ConfigureAwait(false);
            await Run(BuildRemoveArguments(host, stale), cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<bool> WaitUntilRunningAsync(string host, string container, CancellationToken cancellationToken)
    {
        // The poll is otherwise silent for up to ReadinessAttempts × ReadinessDelay — show a status so an
        // interactive user sees it's working (a no-op when stdout is redirected/piped).
        return await Activity.RunAsync(Console, $"Waiting for {container} to become healthy…", async () =>
        {
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
        }).ConfigureAwait(false);
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
            await Console.Error.WriteLineAsync(MaskSecrets(logs.StandardOutput.TrimEnd(), env)).ConfigureAwait(false);
        }

        if (!string.IsNullOrWhiteSpace(logs.StandardError))
        {
            await Console.Error.WriteLineAsync(MaskSecrets(logs.StandardError.TrimEnd(), env)).ConfigureAwait(false);
        }
    }

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

        // Owner-only: it holds the app's secrets, and the temp dir is shared with every other user of the machine.
        var path = Path.Combine(Path.GetTempPath(), $"rask-{slug}-{Guid.NewGuid():N}.env");
        _fileSystem.WriteSecretText(path, string.Join('\n', carriable) + "\n");
        return path;
    }

    private Task<int> Run(IReadOnlyList<string> args, CancellationToken cancellationToken) =>
        _process.RunAsync("docker", args, _workingDirectory, cancellationToken);

    private Task<ProcessResult> Capture(IReadOnlyList<string> args, CancellationToken cancellationToken) =>
        _process.CaptureAsync("docker", args, _workingDirectory, cancellationToken);

    private static string HealthFailureMessage(string healthPath, bool rolledBack) =>
        $"The new container is running but failed its HTTP health check at {healthPath}"
        + (rolledBack ? " — left the previous version serving." : ".")
        + " Fix the app, or deploy with --no-health-check (or --health-path <path> if readiness is served elsewhere).";
}
