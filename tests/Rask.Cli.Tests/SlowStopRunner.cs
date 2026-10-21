namespace Rask.Cli.Tests;

/// <summary>
/// A box whose <c>docker stop</c> of <paramref name="container"/> takes a while: it returns only after the command
/// has read the container's log <paramref name="reportsBeforeExit"/> times, and the log says <paramref name="logs"/>.
/// Everything else is <paramref name="inner"/>, which records every call.
/// </summary>
internal sealed class SlowStopRunner(FakeProcessRunner inner, string container, int reportsBeforeExit, string logs) : IProcessRunner
{
    /// <summary>The line <c>Rask.Jobs</c> logs while it drains, as the simple console formatter writes it.</summary>
    public const string DrainLine =
        "info: Rask.Background.JobProcessor[0]\n      Stopping: no new job is claimed; waiting for 1 running job(s) to finish: AtPump (3m12s)\n";

    private readonly TaskCompletionSource<int> _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _reports;

    public async Task<int> RunAsync(string fileName, IReadOnlyList<string> arguments, string? workingDirectory, CancellationToken cancellationToken, IReadOnlyDictionary<string, string>? environment = null)
    {
        var exit = await inner.RunAsync(fileName, arguments, workingDirectory, cancellationToken, environment).ConfigureAwait(false);
        return arguments.Contains("stop") && arguments[^1] == container ? await _stopped.Task.ConfigureAwait(false) : exit;
    }

    public Task<int> RunTeeAsync(string fileName, IReadOnlyList<string> arguments, string? workingDirectory, Action<string> onLine, CancellationToken cancellationToken, IReadOnlyDictionary<string, string>? environment = null) =>
        inner.RunTeeAsync(fileName, arguments, workingDirectory, onLine, cancellationToken, environment);

    public async Task<ProcessResult> CaptureAsync(string fileName, IReadOnlyList<string> arguments, string? workingDirectory, CancellationToken cancellationToken, IReadOnlyDictionary<string, string>? environment = null)
    {
        var result = await inner.CaptureAsync(fileName, arguments, workingDirectory, cancellationToken, environment).ConfigureAwait(false);
        if (!arguments.Contains("logs") || arguments[^1] != container || _stopped.Task.IsCompleted)
        {
            return result;
        }

        if (Interlocked.Increment(ref _reports) >= reportsBeforeExit)
        {
            _stopped.TrySetResult(0);
        }

        return new ProcessResult(0, logs, string.Empty);
    }
}
