using System.Diagnostics;
using System.Globalization;

namespace Rask.Cli.Tests;

/// <summary>
/// A cancelled call ends the child it started, whichever way it was run: a deploy step past its deadline is cancelled,
/// and only killing the <c>docker</c> client closes the ssh session it holds open on the box.
/// </summary>
public sealed class ProcessRunnerCancellationTests
{
    [Fact]
    public async Task A_cancelled_capture_kills_the_child_it_started()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "the child is a POSIX shell");
        var pidFile = Path.Combine(Path.GetTempPath(), $"rask-capture-{Guid.NewGuid():N}.pid");
        using var cancel = new CancellationTokenSource();

        var capture = new ProcessRunner().CaptureAsync("sh", ["-c", $"echo $$ > '{pidFile}'; exec sleep 60"], Path.GetTempPath(), cancel.Token);
        var pid = await ReadPidAsync(pidFile, capture);
        await cancel.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => capture);
        Assert.True(await GoneAsync(pid), "the child outlived the cancelled capture");
        File.Delete(pidFile);
    }

    [Fact]
    public async Task A_cancelled_tee_kills_the_child_it_started()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "the child is a POSIX shell");
        var pidFile = Path.Combine(Path.GetTempPath(), $"rask-tee-{Guid.NewGuid():N}.pid");
        using var cancel = new CancellationTokenSource();
        var terminal = TextWriter.Synchronized(new StringWriter());

        var tee = new ProcessRunner(terminal, terminal).RunTeeAsync("sh", ["-c", $"echo $$ > '{pidFile}'; exec sleep 60"], Path.GetTempPath(), _ => { }, cancel.Token);
        var pid = await ReadPidAsync(pidFile, tee);
        await cancel.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => tee);
        Assert.True(await GoneAsync(pid), "the child outlived the cancelled tee");
        File.Delete(pidFile);
    }

    // Bounded at a minute: a loaded machine can be slow to start a shell, and a run that ended early says why instead.
    private static async Task<int> ReadPidAsync(string pidFile, Task running)
    {
        for (var i = 0; i < 3000; i++)
        {
            if (running.IsCompleted)
            {
                await running;
                throw new InvalidOperationException("the child exited before it was cancelled");
            }

            if (File.Exists(pidFile) && int.TryParse((await File.ReadAllTextAsync(pidFile)).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var pid))
            {
                return pid;
            }

            await Task.Delay(20);
        }

        throw new TimeoutException("the child never wrote its pid");
    }

    private static async Task<bool> GoneAsync(int pid)
    {
        for (var i = 0; i < 250; i++)
        {
            try
            {
                using var process = Process.GetProcessById(pid);
                if (process.HasExited)
                {
                    return true;
                }
            }
            catch (ArgumentException)
            {
                return true;
            }

            await Task.Delay(20);
        }

        return false;
    }
}
