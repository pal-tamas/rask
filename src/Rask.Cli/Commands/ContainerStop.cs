using static Rask.Cli.Commands.DockerCommands;

namespace Rask.Cli.Commands;

/// <summary>
/// One container's way out within a stop budget: SIGTERM, after which a Rask app claims no new job and finishes the
/// ones it is running; the wait says what it is waiting for, read from the app's log, and a container still busy when
/// the budget runs out is killed — loudly. Shared by <c>rask deploy</c> (the retiring colour) and <c>rask db restore</c>.
/// </summary>
internal sealed class ContainerStop(IConsole console, IProcessRunner process, string workingDirectory, TimeSpan budget)
{
    /// <summary>How often a wait on a stopping container says what it waits for. Zero in tests.</summary>
    public TimeSpan ReportDelay { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>The clock the budget is measured on.</summary>
    public TimeProvider Clock { get; init; } = TimeProvider.System;

    /// <summary>What to do when a job was cut — said with the kill. Defaults to raising <c>stopTimeout</c>.</summary>
    public string? Advice { get; init; }

    private string BudgetText => StopTimeout.Format(budget);

    private string AdviceText => Advice
        ?? $"If it needs longer, raise \"{StopTimeout.Key}\" in .rask/deploy.json (now \"{BudgetText}\").";

    /// <summary>Stop <paramref name="container"/> within the budget. Never removes it.</summary>
    public async Task<StopResult> StopAsync(string host, string container, CancellationToken cancellationToken)
    {
        var started = Clock.GetTimestamp();
        var wait = new StopWait(container);
        while (true)
        {
            // Whole seconds, rounded up: `docker stop -t` takes an integer, and the first stop gets the whole budget.
            var remaining = TimeSpan.FromSeconds(Math.Ceiling((budget - Clock.GetElapsedTime(started)).TotalSeconds));
            var exit = remaining > TimeSpan.Zero
                ? await StopReportingAsync(host, wait, started, remaining, cancellationToken).ConfigureAwait(false)
                : await Run(BuildKillArguments(host, container), cancellationToken).ConfigureAwait(false);
            if (exit == 0)
            {
                break;
            }

            // `docker stop` failed: the container was not there, or the connection to the box dropped mid-wait.
            var state = await StateAsync(host, container, cancellationToken).ConfigureAwait(false);
            if (state is { } gone && gone != StopResult.Stopped)
            {
                return gone;
            }

            if (state == StopResult.Stopped)
            {
                break;
            }

            console.WriteErrorLine($"  ! `docker stop` ended early (exit {exit}) while {container} is still running — stopping it again.", ConsoleStyle.Warning);
            if (ReportDelay > TimeSpan.Zero)
            {
                await Task.Delay(ReportDelay, cancellationToken).ConfigureAwait(false);
            }
        }

        await SayIfKilledAsync(host, wait, cancellationToken).ConfigureAwait(false);
        return StopResult.Stopped;
    }

    // Runs `docker stop`, and while it waits, reads the container's log for what it is still finishing and says so.
    private async Task<int> StopReportingAsync(string host, StopWait wait, long started, TimeSpan remaining, CancellationToken cancellationToken)
    {
        var stopping = Run(BuildStopArguments(host, wait.Container, remaining), cancellationToken);
        while (!stopping.IsCompleted)
        {
            await Task.WhenAny(stopping, Task.Delay(ReportDelay, cancellationToken)).ConfigureAwait(false);
            if (stopping.IsCompleted)
            {
                break;
            }

            if (!wait.Announced)
            {
                console.WriteLine($"Stopping {wait.Container}: it claims no new job and finishes the ones it is running (up to {BudgetText})…", ConsoleStyle.Dim);
                wait.Announced = true;
            }

            var logs = await Capture(BuildLogsArguments(host, wait.Container), cancellationToken).ConfigureAwait(false);
            var jobs = StopProgress.RunningJobs(logs.StandardOutput + "\n" + logs.StandardError);
            wait.LastSeen = jobs ?? wait.LastSeen;
            var waited = $"waited {StopTimeout.Format(TimeSpan.FromSeconds((int)Clock.GetElapsedTime(started).TotalSeconds))} of {BudgetText}";
            await console.Out.WriteLineAsync(jobs is null ? $"  {wait.Container} is still stopping — {waited}" : $"  {wait.Container} is finishing {jobs} — {waited}").ConfigureAwait(false);
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

        var finishing = wait.LastSeen is null ? string.Empty : $" — it was finishing {wait.LastSeen}";
        console.WriteErrorLine($"{wait.Container} was still running when its {BudgetText} stop budget ran out and was killed{finishing}.", ConsoleStyle.Error);
        console.WriteErrorLine($"  A job cut this way runs again from the start, under its retry rules. {AdviceText}", ConsoleStyle.Error);
    }

    private async Task<StopResult?> StateAsync(string host, string container, CancellationToken cancellationToken)
    {
        var inspected = await Capture(BuildInspectRunningArguments(host, container), cancellationToken).ConfigureAwait(false);
        if (inspected.ExitCode == 0)
        {
            // Null: still running, so the stop is tried again.
            return inspected.StandardOutput.Trim().Equals("true", StringComparison.OrdinalIgnoreCase) ? null : StopResult.Stopped;
        }

        return inspected.StandardError.Contains("No such", StringComparison.OrdinalIgnoreCase) ? StopResult.Absent : StopResult.Unknown;
    }

    private Task<int> Run(IReadOnlyList<string> args, CancellationToken cancellationToken) =>
        process.RunAsync("docker", args, workingDirectory, cancellationToken);

    private Task<ProcessResult> Capture(IReadOnlyList<string> args, CancellationToken cancellationToken) =>
        process.CaptureAsync("docker", args, workingDirectory, cancellationToken);

    /// <summary>One container's way out: whether the wait was announced, and the last thing it said it was finishing.</summary>
    private sealed class StopWait(string container)
    {
        public string Container { get; } = container;

        public bool Announced { get; set; }

        public string? LastSeen { get; set; }
    }
}
