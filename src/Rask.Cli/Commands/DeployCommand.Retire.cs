using static Rask.Cli.Commands.DockerCommands;

namespace Rask.Cli.Commands;

/// <summary>
/// The container a deploy replaces goes the way the app's stop budget allows: SIGTERM, after which it claims no new
/// job and finishes the ones it is running; the deploy waits for it to exit, saying what it waits for, and only a
/// container still busy when the budget runs out is killed — loudly. See <see cref="ContainerStop"/>.
/// </summary>
internal sealed partial class DeployCommand
{
    /// <summary>How often a deploy waiting on a stopping container says what it waits for. Zero in tests.</summary>
    internal TimeSpan StopReportDelay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>What a port-mode deploy says when it had to kill a busy container: its budget is not the knob.</summary>
    private const string PortModeAdvice = "Deploy with --domain to let running jobs finish before the old container stops.";

    /// <summary>The clock the stop budget is measured on.</summary>
    internal TimeProvider Clock { get; set; } = TimeProvider.System;

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
            await RetireContainerAsync(host, container, StopBudget, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Stop <paramref name="container"/> within <paramref name="budget"/>, then remove it. A no-op for a container that
    /// isn't there. One that could not be confirmed stopped (the box stopped answering) is left as it is, said so.
    /// </summary>
    private async Task RetireContainerAsync(string host, string container, TimeSpan budget, CancellationToken cancellationToken, string? advice = null)
    {
        var stop = new ContainerStop(Console, _process, _workingDirectory, budget) { ReportDelay = StopReportDelay, Clock = Clock, Advice = advice };
        if (await stop.StopAsync(host, container, cancellationToken).ConfigureAwait(false) == StopResult.Unknown)
        {
            Console.WriteErrorLine($"  ! Lost touch with {HostName(host)} while {container} was stopping — it was left to finish on its own. Check `rask deploy status`, and remove it once it has exited: docker -H ssh://{host} rm {container}", ConsoleStyle.Warning);
            return;
        }

        await Run(BuildRemoveArguments(host, container), cancellationToken).ConfigureAwait(false); // ignore-absent
    }
}
