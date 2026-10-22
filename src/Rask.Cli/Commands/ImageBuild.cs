namespace Rask.Cli.Commands;

/// <summary>
/// Runs <c>docker build</c> on the box with its output passed through, held to <see cref="DeployDeadlines.Build"/> in all
/// and to <see cref="DeployDeadlines.BuildIdle"/> without a line, and says so while it is quiet — so a download that stalls
/// inside the build, or an ssh session that stops carrying anything, ends the deploy instead of holding it open.
/// </summary>
internal sealed class ImageBuild(IConsole console, IProcessRunner process, string workingDirectory, DeployDeadlines deadlines)
{
    /// <summary>The clock both deadlines are measured on.</summary>
    public TimeProvider Clock { get; init; } = TimeProvider.System;

    /// <summary>The line it printed last, for the message when it went quiet.</summary>
    public string? LastLine { get; private set; }

    /// <summary>Build with <paramref name="arguments"/>; a build stopped at a deadline has been killed, with its ssh session.</summary>
    public async Task<BuildEnd> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var started = Clock.GetTimestamp();
        var heard = started;
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var building = process.RunTeeAsync("docker", arguments, workingDirectory, line =>
        {
            Interlocked.Exchange(ref heard, Clock.GetTimestamp());
            LastLine = line;
        }, stop.Token);

        BuildEnd? stopped = null;
        while (!building.IsCompleted)
        {
            await Task.WhenAny(building, Task.Delay(deadlines.Progress, cancellationToken)).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (building.IsCompleted)
            {
                break;
            }

            var elapsed = Clock.GetElapsedTime(started);
            var quiet = Clock.GetElapsedTime(Interlocked.Read(ref heard));
            stopped = Overdue(elapsed, quiet);
            if (stopped is not null)
            {
                await stop.CancelAsync().ConfigureAwait(false);
                break;
            }

            if (quiet >= deadlines.Progress)
            {
                console.WriteLine(
                    $"  … still building — {Minutes(elapsed)} in, nothing printed for {Minutes(quiet)} (stops after {Minutes(deadlines.BuildIdle)} of silence)",
                    ConsoleStyle.Dim);
            }
        }

        try
        {
            return await building.ConfigureAwait(false) == 0 ? BuildEnd.Built : stopped ?? BuildEnd.Failed;
        }
        catch (OperationCanceledException) when (stopped is not null && !cancellationToken.IsCancellationRequested)
        {
            return stopped.Value;
        }
    }

    private BuildEnd? Overdue(TimeSpan elapsed, TimeSpan quiet)
    {
        if (elapsed >= deadlines.Build)
        {
            return BuildEnd.TimedOut;
        }

        return quiet >= deadlines.BuildIdle ? BuildEnd.WentQuiet : null;
    }

    private static string Minutes(TimeSpan span) => StopTimeout.Format(TimeSpan.FromSeconds((int)span.TotalSeconds));
}
