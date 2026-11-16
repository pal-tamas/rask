namespace Rask.Cli.Tests;

/// <summary>
/// A <see cref="FakeProcessRunner"/> whose forwarding <c>ssh -L</c> stays up until it is cancelled, as the real one does —
/// so a deploy that opens a tunnel has one to use, and a test can see that it was closed at the end.
/// </summary>
internal sealed class HeldSshRunner(FakeProcessRunner inner) : IProcessRunner
{
    private int _opened;
    private int _closed;

    public int TunnelsOpened => Volatile.Read(ref _opened);

    public int TunnelsClosed => Volatile.Read(ref _closed);

    public async Task<int> RunAsync(string fileName, IReadOnlyList<string> arguments, string? workingDirectory, CancellationToken cancellationToken, IReadOnlyDictionary<string, string>? environment = null)
    {
        if (!string.Equals(fileName, "ssh", StringComparison.Ordinal) || !arguments.Contains("-L", StringComparer.Ordinal))
        {
            return await inner.RunAsync(fileName, arguments, workingDirectory, cancellationToken, environment).ConfigureAwait(false);
        }

        Interlocked.Increment(ref _opened);
        await inner.RunAsync(fileName, arguments, workingDirectory, CancellationToken.None, environment).ConfigureAwait(false);
        try
        {
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            return 0;
        }
        finally
        {
            Interlocked.Increment(ref _closed);
        }
    }

    public Task<int> PipeAsync(string fileName, IReadOnlyList<string> source, IReadOnlyList<string> sink, string? workingDirectory, CancellationToken cancellationToken) =>
        inner.PipeAsync(fileName, source, sink, workingDirectory, cancellationToken);

    public Task<ProcessResult> CaptureAsync(string fileName, IReadOnlyList<string> arguments, string? workingDirectory, CancellationToken cancellationToken, IReadOnlyDictionary<string, string>? environment = null) =>
        inner.CaptureAsync(fileName, arguments, workingDirectory, cancellationToken, environment);

    public Task<int> RunTeeAsync(string fileName, IReadOnlyList<string> arguments, string? workingDirectory, Action<string> onLine, CancellationToken cancellationToken, IReadOnlyDictionary<string, string>? environment = null) =>
        inner.RunTeeAsync(fileName, arguments, workingDirectory, onLine, cancellationToken, environment);
}
