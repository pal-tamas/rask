using static Rask.Cli.Commands.DockerCommands;

namespace Rask.Cli.Commands;

/// <summary>
/// The container a deploy replaces goes the way the app's stop budget allows: SIGTERM, after which it claims no new
/// job and finishes the ones it is running; the deploy waits for it to exit, saying what it waits for, and only a
/// container still busy when <see cref="StopBudget"/> runs out is killed — loudly.
/// </summary>
internal sealed partial class DeployCommand
{
    /// <summary>How often a deploy waiting on a stopping container says what it waits for. Zero in tests.</summary>
    internal TimeSpan StopReportDelay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>The clock the stop budget is measured on.</summary>
    internal TimeProvider Clock { get; set; } = TimeProvider.System;

    /// <summary>What the box says about a container: running, gone or exited, or nothing (it could not be asked).</summary>
    private enum ContainerState
    {
        Running,
        Stopped,
        Unknown,
    }

    /// <summary>Stops and removes this app's other colours once traffic is on the new one.</summary>
    private async Task RetireOldColorsAsync(string host, string slug, string newContainer, CancellationToken cancellationToken)
    {
        // Let the proxy finish with the old colour before pulling it out from under itself: `caddy reload` returns
        // before Caddy has let go of its pooled keep-alive connections to the old upstream. See PreStopDrainSeconds.
        if (PreStopDrainDelay > TimeSpan.Zero)
        {
            await Task.Delay(PreStopDrainDelay, cancellationToken).ConfigureAwait(false);
        }

        // Listed again: only this app's deploys touch its colours, and this deploy holds the app's lock.
        if (await ListAppsAsync(host, cancellationToken).ConfigureAwait(false) is not { } apps)
        {
            Console.WriteErrorLine($"  ! Could not list the containers on {HostName(host)} — the previous colour of {slug} was left running. Stop it with `docker -H ssh://{host} stop -t {(int)StopBudget.TotalSeconds} {slug}-<colour>`.", ConsoleStyle.Warning);
            return;
        }

        var stale = apps
            .Where(a => string.Equals(a.App, slug, StringComparison.Ordinal))
            .Select(a => a.Container)
            .Where(container => !string.Equals(container, newContainer, StringComparison.Ordinal));
        foreach (var container in stale)
        {
            await RetireContainerAsync(host, container, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Stop <paramref name="container"/> within <see cref="StopBudget"/>, then remove it. A no-op for a container that
    /// isn't there. One that could not be confirmed stopped (the box stopped answering) is left as it is, said so.
    /// </summary>
    private async Task RetireContainerAsync(string host, string container, CancellationToken cancellationToken)
    {
        var started = Clock.GetTimestamp();
        var wait = new StopWait(container);
        while (true)
        {
            // Whole seconds, rounded up: `docker stop -t` takes an integer, and the first stop gets the whole budget.
            var remaining = TimeSpan.FromSeconds(Math.Ceiling((StopBudget - Clock.GetElapsedTime(started)).TotalSeconds));
            var exit = remaining > TimeSpan.Zero
                ? await StopReportingAsync(host, wait, started, remaining, cancellationToken).ConfigureAwait(false)
                : await Run(BuildKillArguments(host, container), cancellationToken).ConfigureAwait(false);
            if (exit == 0)
            {
                break;
            }

            // `docker stop` failed: the container was not there, or the connection to the box dropped mid-wait.
            var state = await StateAsync(host, container, cancellationToken).ConfigureAwait(false);
            if (state == ContainerState.Stopped)
            {
                break;
            }

            if (state == ContainerState.Unknown)
            {
                Console.WriteErrorLine($"  ! Lost touch with {HostName(host)} while {container} was stopping — it was left to finish on its own. Check `rask deploy status`, and remove it once it has exited: docker -H ssh://{host} rm {container}", ConsoleStyle.Warning);
                return;
            }

            Console.WriteErrorLine($"  ! `docker stop` ended early (exit {exit}) while {container} is still running — stopping it again.", ConsoleStyle.Warning);
            if (StopReportDelay > TimeSpan.Zero)
            {
                await Task.Delay(StopReportDelay, cancellationToken).ConfigureAwait(false);
            }
        }

        await SayIfKilledAsync(host, wait, cancellationToken).ConfigureAwait(false);
        await Run(BuildRemoveArguments(host, container), cancellationToken).ConfigureAwait(false); // ignore-absent
    }

    // Runs `docker stop`, and while it waits, reads the container's log for what it is still finishing and says so.
    private async Task<int> StopReportingAsync(string host, StopWait wait, long started, TimeSpan budget, CancellationToken cancellationToken)
    {
        var stopping = Run(BuildStopArguments(host, wait.Container, budget), cancellationToken);
        while (!stopping.IsCompleted)
        {
            await Task.WhenAny(stopping, Task.Delay(StopReportDelay, cancellationToken)).ConfigureAwait(false);
            if (stopping.IsCompleted)
            {
                break;
            }

            if (!wait.Announced)
            {
                Console.WriteLine($"Stopping {wait.Container}: it claims no new job and finishes the ones it is running (up to {StopTimeout.Format(StopBudget)}, \"{StopTimeout.Key}\" in .rask/deploy.json)…", ConsoleStyle.Dim);
                wait.Announced = true;
            }

            var logs = await Capture(BuildLogsArguments(host, wait.Container), cancellationToken).ConfigureAwait(false);
            var jobs = StopProgress.RunningJobs(logs.StandardOutput + "\n" + logs.StandardError);
            wait.LastSeen = jobs ?? wait.LastSeen;
            var waited = $"waited {StopTimeout.Format(TimeSpan.FromSeconds((int)Clock.GetElapsedTime(started).TotalSeconds))} of {StopTimeout.Format(StopBudget)}";
            await Console.Out.WriteLineAsync(jobs is null ? $"  {wait.Container} is still stopping — {waited}" : $"  {wait.Container} is finishing {jobs} — {waited}").ConfigureAwait(false);
        }

        return await stopping.ConfigureAwait(false);
    }

    // 137 is SIGKILL: the budget ran out with the app still at work, and whatever job it was running was cut.
    private async Task SayIfKilledAsync(string host, StopWait wait, CancellationToken cancellationToken)
    {
        var ended = await Capture(BuildExitCodeArguments(host, wait.Container), cancellationToken).ConfigureAwait(false);
        if (ended.ExitCode != 0 || !string.Equals(ended.StandardOutput.Trim(), "137", StringComparison.Ordinal))
        {
            return;
        }

        var budget = StopTimeout.Format(StopBudget);
        var finishing = wait.LastSeen is null ? string.Empty : $" — it was finishing {wait.LastSeen}";
        Console.WriteErrorLine($"{wait.Container} was still running when its {budget} stop budget ran out and was killed{finishing}.", ConsoleStyle.Error);
        Console.WriteErrorLine($"  A job cut this way runs again from the start on the new container, under its retry rules. If it needs longer, raise \"{StopTimeout.Key}\" in .rask/deploy.json (now \"{budget}\").", ConsoleStyle.Error);
    }

    private async Task<ContainerState> StateAsync(string host, string container, CancellationToken cancellationToken)
    {
        var inspected = await Capture(BuildInspectRunningArguments(host, container), cancellationToken).ConfigureAwait(false);
        if (inspected.ExitCode == 0)
        {
            return inspected.StandardOutput.Trim().Equals("true", StringComparison.OrdinalIgnoreCase) ? ContainerState.Running : ContainerState.Stopped;
        }

        return inspected.StandardError.Contains("No such", StringComparison.OrdinalIgnoreCase) ? ContainerState.Stopped : ContainerState.Unknown;
    }

    /// <summary>One container's way out: whether the wait was announced, and the last thing it said it was finishing.</summary>
    private sealed class StopWait(string container)
    {
        public string Container { get; } = container;

        public bool Announced { get; set; }

        public string? LastSeen { get; set; }
    }
}
