namespace Rask.Cli;

/// <summary>
/// A tiny, dependency-free progress spinner for otherwise-silent long operations (e.g. polling a
/// remote container until it's healthy). It animates a single line in place while the work runs and
/// clears it on dispose. It is a **no-op when stdout is redirected** — piped output and captured test
/// output stay byte-for-byte unchanged, and CI logs don't fill with carriage returns. Use it with
/// <c>await using</c> so the line is always cleared, even on an exception.
/// </summary>
internal sealed class Spinner : IAsyncDisposable
{
    private static readonly string[] Frames = ["⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏"];

    private readonly TextWriter _out;
    private readonly string _message;
    private readonly bool _enabled;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task? _loop;

    private Spinner(IConsole console, string message)
    {
        _out = console.Out;
        _message = message;
        _enabled = !console.IsOutputRedirected;
        if (_enabled)
        {
            _loop = RunAsync();
        }
    }

    /// <summary>Begin spinning with <paramref name="message"/>. Returns immediately; dispose to stop and clear.</summary>
    public static Spinner Start(IConsole console, string message) => new(console, message);

    private async Task RunAsync()
    {
        var frame = 0;
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                _out.Write($"\r{Frames[frame++ % Frames.Length]} {_message}");
                _out.Flush();
                await Task.Delay(80, _cts.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Disposed — fall through and let DisposeAsync clear the line.
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (!_enabled)
        {
            _cts.Dispose();
            return;
        }

        await _cts.CancelAsync().ConfigureAwait(false);
        if (_loop is not null)
        {
            try
            {
                await _loop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected on cancellation.
            }
        }

        // Erase the spinner line so the next write starts clean.
        _out.Write('\r' + new string(' ', _message.Length + 2) + '\r');
        _out.Flush();
        _cts.Dispose();
    }
}
