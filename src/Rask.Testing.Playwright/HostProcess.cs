using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;

namespace Rask.Testing;

/// <summary>
///     The app, running as a process of its own: what it writes is kept, and disposing it ends it — and every process
///     it started.
/// </summary>
internal sealed class HostProcess : IAsyncDisposable
{
    private static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan StopWithin = TimeSpan.FromSeconds(10);

    // A test run that ends without disposing its site (a crash, a runner that does not dispose fixtures) still takes its
    // apps with it: an app left listening holds its port and its database files until someone finds it.
    private static readonly ConcurrentDictionary<HostProcess, byte> Running = new();

    private readonly Process _process;
    private readonly ConcurrentQueue<string> _said = new();

    static HostProcess() => AppDomain.CurrentDomain.ProcessExit += (_, _) =>
    {
        foreach (var host in Running.Keys)
        {
            host.Kill();
        }
    };

    private HostProcess(Process process) => _process = process;

    /// <summary>Everything the app has written so far, standard output and error together, line by line.</summary>
    public string Output => string.Join(Environment.NewLine, _said);

    /// <summary>Whether the process has ended.</summary>
    public bool HasExited => _process.HasExited;

    /// <summary>Starts <paramref name="start" />, keeping what it writes.</summary>
    /// <exception cref="InvalidOperationException">The process could not be started.</exception>
    public static HostProcess Start(ProcessStartInfo start)
    {
        ArgumentNullException.ThrowIfNull(start);
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;
        start.UseShellExecute = false;

        var process = new Process { StartInfo = start, EnableRaisingEvents = true };
        var host = new HostProcess(process);
        process.OutputDataReceived += (_, line) => host.Heard(line.Data);
        process.ErrorDataReceived += (_, line) => host.Heard(line.Data);
        if (!process.Start())
        {
            process.Dispose();
            throw new InvalidOperationException($"{start.FileName} {string.Join(' ', start.ArgumentList)} did not start.");
        }

        Running[host] = 0;
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return host;
    }

    /// <summary>Whether the app has written <paramref name="text" /> since it started.</summary>
    public bool Said(string text) => _said.Any(line => line.Contains(text, StringComparison.Ordinal));

    /// <summary>
    ///     Waits until <paramref name="health" /> answers with a success status: <c>true</c> once it does, <c>false</c> when
    ///     the app stopped because its port was taken — which the caller answers by starting again on another.
    /// </summary>
    /// <exception cref="InvalidOperationException">The app stopped for any other reason; the message holds what it said.</exception>
    /// <exception cref="TimeoutException">It did not answer within <paramref name="within" />.</exception>
    public async Task<bool> Answering(Uri health, TimeSpan within, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(health);
        using var http = new HttpClient();
        http.Timeout = TimeSpan.FromSeconds(5);
        var started = Stopwatch.StartNew();
        HttpStatusCode? last = null;
        while (started.Elapsed < within)
        {
            if (_process.HasExited)
            {
                // The output is read on threads of its own: let them finish, or the line that says why is not in yet.
                await _process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
                if (LoopbackPort.LooksLikeAddressInUse(Output))
                {
                    return false;
                }

                throw new InvalidOperationException(
                    $"The app stopped as it started (exit code {_process.ExitCode}). It said:{Environment.NewLine}{Output}");
            }

            try
            {
                using var answer = await http.GetAsync(health, cancellationToken).ConfigureAwait(false);
                if (answer.IsSuccessStatusCode)
                {
                    return true;
                }

                last = answer.StatusCode;
            }
            catch (HttpRequestException)
            {
                // not listening yet
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // listening, and too busy starting to answer in time
            }

            await Task.Delay(Poll, cancellationToken).ConfigureAwait(false);
        }

        var heard = last is { } status ? $"it last answered {(int)status} {status}" : "nothing answered there";
        throw new TimeoutException(
            $"The app did not answer {health} within {within.TotalSeconds:0} s: {heard}. A RaskApp answers its health check "
            + $"at /health; an app that maps another says which: site.Health = \"/version\". It said:{Environment.NewLine}{Output}");
    }

    /// <summary>Ends the app and every process it started, and waits for it to go.</summary>
    public async ValueTask DisposeAsync()
    {
        Running.TryRemove(this, out _);
        Kill();
        using var stopping = new CancellationTokenSource(StopWithin);
        try
        {
            await _process.WaitForExitAsync(stopping.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // it would not go: the OS takes it with this process
        }

        _process.Dispose();
    }

    private void Heard(string? line)
    {
        if (line is not null)
        {
            _said.Enqueue(line);
        }
    }

    private void Kill()
    {
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception gone) when (gone is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // it exited between the check and the kill
        }
    }
}
