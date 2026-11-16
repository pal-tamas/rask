namespace Rask.Cli.Commands;

/// <summary>
/// Puts a deadline on every <c>docker</c> call a deploy makes (<see cref="DeployDeadlines.For"/>): past it, the call is
/// cancelled — the runner kills the client, and with it the ssh session — and a <see cref="TimeoutException"/> names
/// the step that stalled. Anything that is not <c>docker</c> (the host probe and setup over plain ssh) passes through.
/// </summary>
internal sealed class DeadlineRunner(IProcessRunner inner) : IProcessRunner
{
    public DeployDeadlines Deadlines { get; set; } = DeployDeadlines.Default;

    public Task<int> RunAsync(string fileName, IReadOnlyList<string> arguments, string? workingDirectory, CancellationToken cancellationToken, IReadOnlyDictionary<string, string>? environment = null) =>
        WithDeadline(fileName, arguments, token => inner.RunAsync(fileName, arguments, workingDirectory, token, environment), cancellationToken);

    public Task<ProcessResult> CaptureAsync(string fileName, IReadOnlyList<string> arguments, string? workingDirectory, CancellationToken cancellationToken, IReadOnlyDictionary<string, string>? environment = null) =>
        WithDeadline(fileName, arguments, token => inner.CaptureAsync(fileName, arguments, workingDirectory, token, environment), cancellationToken);

    // The build is the one teed call, and ImageBuild holds it to both of its deadlines itself.
    public Task<int> RunTeeAsync(string fileName, IReadOnlyList<string> arguments, string? workingDirectory, Action<string> onLine, CancellationToken cancellationToken, IReadOnlyDictionary<string, string>? environment = null) =>
        inner.RunTeeAsync(fileName, arguments, workingDirectory, onLine, cancellationToken, environment);

    // The image shipment: held to the deadline of its receiving end (a `load`, as long as a build may take).
    public Task<int> PipeAsync(string fileName, IReadOnlyList<string> source, IReadOnlyList<string> sink, string? workingDirectory, CancellationToken cancellationToken) =>
        WithDeadline(fileName, sink, token => inner.PipeAsync(fileName, source, sink, workingDirectory, token), cancellationToken);

    private async Task<T> WithDeadline<T>(string fileName, IReadOnlyList<string> arguments, Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        if (!string.Equals(fileName, "docker", StringComparison.Ordinal) || Deadlines.For(arguments) is not { } deadline)
        {
            return await call(cancellationToken).ConfigureAwait(false);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(deadline);
        try
        {
            return await call(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException e) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"`docker {DeployDeadlines.Verb(arguments)}` did not finish within {StopTimeout.Format(deadline)}", e);
        }
    }
}
