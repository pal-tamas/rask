namespace Rask.Cli.Tests;

/// <summary>
/// A box that stops answering one command: a call <paramref name="stalls"/> matches prints <paramref name="lines"/> (a
/// teed build's output) and then never returns until it is cancelled — as a <c>docker -H ssh://</c> does when a download
/// inside the build stalls, or the ssh session stops carrying anything. Everything else is <paramref name="inner"/>.
/// </summary>
internal sealed class StallingRunner(IProcessRunner inner, Func<IReadOnlyList<string>, bool> stalls, params string[] lines) : IProcessRunner
{
    /// <summary>Whether a stalled call was cancelled — what kills the real client, and its ssh session with it.</summary>
    public bool Cancelled { get; private set; }

    public async Task<int> RunAsync(string fileName, IReadOnlyList<string> arguments, string? workingDirectory, CancellationToken cancellationToken, IReadOnlyDictionary<string, string>? environment = null)
    {
        if (stalls(arguments))
        {
            await StallAsync(cancellationToken).ConfigureAwait(false);
        }

        return await inner.RunAsync(fileName, arguments, workingDirectory, cancellationToken, environment).ConfigureAwait(false);
    }

    public async Task<int> RunTeeAsync(string fileName, IReadOnlyList<string> arguments, string? workingDirectory, Action<string> onLine, CancellationToken cancellationToken, IReadOnlyDictionary<string, string>? environment = null)
    {
        if (stalls(arguments))
        {
            foreach (var line in lines)
            {
                onLine(line);
            }

            await StallAsync(cancellationToken).ConfigureAwait(false);
        }

        return await inner.RunTeeAsync(fileName, arguments, workingDirectory, onLine, cancellationToken, environment).ConfigureAwait(false);
    }

    public async Task<ProcessResult> CaptureAsync(string fileName, IReadOnlyList<string> arguments, string? workingDirectory, CancellationToken cancellationToken, IReadOnlyDictionary<string, string>? environment = null)
    {
        if (stalls(arguments))
        {
            await StallAsync(cancellationToken).ConfigureAwait(false);
        }

        return await inner.CaptureAsync(fileName, arguments, workingDirectory, cancellationToken, environment).ConfigureAwait(false);
    }

    private async Task StallAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            Cancelled = true;
            throw;
        }
    }
}
