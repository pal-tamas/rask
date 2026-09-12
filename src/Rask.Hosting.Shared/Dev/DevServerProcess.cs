using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Net.Sockets;

namespace Rask.Hosting.Shared;

/// <summary>
///     A front-end dev server — Vite for islands, a framework's own <c>npm run dev</c> — that an app runs as
///     its child during an editor-launched dev session, where there is no <c>rask dev</c> to start it beside
///     the app.
/// </summary>
/// <remarks>
///     <para>
///         Stopped by killing its whole process tree, never by a signal to npm alone: <c>npm run</c> starts the
///         real server as a grandchild, so ending npm would leave the server holding its port. A dev server
///         has no in-flight work worth draining, which is what separates this from the production Node
///         supervisor and its SIGTERM grace period.
///     </para>
///     <para>
///         A debugger's Stop kills the app outright, so nothing here runs and the dev server is left behind —
///         still on its port, where the next F5's server would fail to bind. So each one records its pid and
///         start time, and the next start ends a recorded process that is still alive AND still the same
///         process. A pid alone is not enough: by then it can belong to something unrelated, and killing that
///         would be far worse than a port clash.
///     </para>
/// </remarks>
internal sealed class DevServerProcess : IAsyncDisposable
{
    private readonly Process _process;
    private readonly string _pidFile;

    private DevServerProcess(Process process, string pidFile)
    {
        _process = process;
        _pidFile = pidFile;
    }

    internal int Id => _process.Id;

    internal bool HasExited => _process.HasExited;

    internal int ExitCode => _process.ExitCode;

    /// <summary>
    ///     Ends a dev server left behind by an earlier run, then starts this one and records it.
    /// </summary>
    /// <param name="name">Names the record, one per kind of dev server (<c>islands-vite</c>, <c>spa-dev</c>).</param>
    /// <param name="fileName">The executable: <c>npm</c> or <c>npx</c>.</param>
    /// <param name="arguments">Its arguments.</param>
    /// <param name="workingDirectory">Where it runs.</param>
    /// <param name="stateDirectory">Where the record is kept — under the app's <c>obj/</c>, so it is never committed.</param>
    /// <param name="onLine">Receives each line the process writes, stdout and stderr alike.</param>
    /// <exception cref="Win32Exception">The executable could not be started (Node is not installed).</exception>
    internal static DevServerProcess Start(
        string name,
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        string stateDirectory,
        Action<string> onLine)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentException.ThrowIfNullOrEmpty(fileName);
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(onLine);

        var pidFile = Path.Combine(stateDirectory, name + ".pid");
        ReclaimOrphan(pidFile);

        var info = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        var process = new Process { StartInfo = info, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) => Forward(e.Data, onLine);
        process.ErrorDataReceived += (_, e) => Forward(e.Data, onLine);

        try
        {
            process.Start();
        }
        catch
        {
            process.Dispose();
            throw;
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        WritePidFile(pidFile, process);

        return new DevServerProcess(process, pidFile);
    }

    internal Task WaitForExitAsync(CancellationToken cancellationToken) => _process.WaitForExitAsync(cancellationToken);

    /// <summary>Kills the process tree and forgets the record.</summary>
    public async ValueTask DisposeAsync()
    {
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);

                using var grace = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await _process.WaitForExitAsync(grace.Token).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException
                                       or OperationCanceledException)
        {
            // Exited between the check and the kill, or a member of the tree refused the signal. The app is
            // stopping either way, and a leftover is reclaimed by the next start.
        }
        finally
        {
            TryDelete(_pidFile);
            _process.Dispose();
        }
    }

    /// <summary>
    ///     Ends the process <paramref name="pidFile" /> records, if it is still running and is still the same
    ///     process. Returns whether one was ended. The record is removed either way.
    /// </summary>
    internal static bool ReclaimOrphan(string pidFile)
    {
        if (!TryReadPidFile(pidFile, out var pid, out var startedTicks))
        {
            return false;
        }

        try
        {
            using var orphan = Process.GetProcessById(pid);
            if (StartTicks(orphan) != startedTicks)
            {
                // The pid was reused by something else since the record was written. Not ours to end.
                return false;
            }

            orphan.Kill(entireProcessTree: true);
            orphan.WaitForExit(TimeSpan.FromSeconds(5));
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception
                                       or NotSupportedException)
        {
            // Not running any more, or not ours to see. Nothing to end.
            return false;
        }
        finally
        {
            TryDelete(pidFile);
        }
    }

    /// <summary>Records <paramref name="process" /> so the next start can end it if a debugger's Stop orphans it.</summary>
    internal static void WritePidFile(string pidFile, Process process)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(pidFile)!);
            File.WriteAllText(
                pidFile,
                string.Create(CultureInfo.InvariantCulture, $"{process.Id} {StartTicks(process)}"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException
                                       or Win32Exception)
        {
            // A missing record costs one port clash after a hard stop, not this run.
        }
    }

    /// <summary>
    ///     Polls a connect to <paramref name="host" />:<paramref name="port" /> until something accepts, the
    ///     process exits, or the time runs out.
    /// </summary>
    /// <remarks>
    ///     <c>localhost</c> rather than <c>127.0.0.1</c> by default at the call sites: Node resolves
    ///     <c>localhost</c> to <c>::1</c> first, so a dev server bound that way answers on IPv6 only, and a
    ///     connect to the name tries both families.
    /// </remarks>
    internal static async Task<bool> WaitForPortAsync(
        string host,
        int port,
        Func<bool> hasExited,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(hasExited);

        var deadline = DateTimeOffset.UtcNow + timeout;

        while (!cancellationToken.IsCancellationRequested && DateTimeOffset.UtcNow < deadline)
        {
            if (hasExited())
            {
                return false;
            }

            try
            {
                using var probe = new TcpClient();
                await probe.ConnectAsync(host, port, cancellationToken).ConfigureAwait(false);

                // A connect proves something owns the port, not that our child does.
                return !hasExited();
            }
            catch (SocketException)
            {
                // Not listening yet.
            }
            catch (OperationCanceledException)
            {
                return false;
            }

            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }

        return false;
    }

    private static long StartTicks(Process process) => process.StartTime.ToUniversalTime().Ticks;

    private static void Forward(string? line, Action<string> onLine)
    {
        if (!string.IsNullOrWhiteSpace(line))
        {
            onLine(line);
        }
    }

    private static bool TryReadPidFile(string pidFile, out int pid, out long startedTicks)
    {
        pid = 0;
        startedTicks = 0;

        try
        {
            if (!File.Exists(pidFile))
            {
                return false;
            }

            var parts = File.ReadAllText(pidFile)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            return parts.Length == 2
                   && int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out pid)
                   && long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out startedTicks);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A stale record is harmless: the next start checks the start time before ending anything.
        }
    }
}
