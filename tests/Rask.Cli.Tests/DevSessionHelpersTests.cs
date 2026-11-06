using System.Diagnostics;
using Rask.Hosting.Shared;

namespace Rask.Cli.Tests;

/// <summary>
///     The dev-session helpers <c>rask dev</c> and an editor-launched app share: the dev-server process that
///     has to survive a debugger's hard stop without leaving a port taken.
/// </summary>
public sealed class DevSessionHelpersTests
{
    // ---- orphaned dev servers ----

    [Fact]
    public async Task A_dev_server_a_hard_stop_left_behind_is_ended_by_the_next_start()
    {
        if (OperatingSystem.IsWindows())
        {
            return; // `sleep` is a Unix binary; the kill-tree path is the same on Windows.
        }

        using var project = new TempProject();
        var pidFile = Path.Combine(project.Path, "obj", "rask", "spa-dev.pid");
        using var orphan = Process.Start(new ProcessStartInfo("sleep", "60") { UseShellExecute = false })!;
        DevServerProcess.WritePidFile(pidFile, orphan);

        Assert.True(DevServerProcess.ReclaimOrphan(pidFile));

        await orphan.WaitForExitAsync(new CancellationTokenSource(TimeSpan.FromSeconds(10)).Token);

        Assert.True(orphan.HasExited);
        Assert.False(File.Exists(pidFile));
    }

    [Fact]
    public void A_reused_pid_is_never_ended()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var project = new TempProject();
        var pidFile = Path.Combine(project.Path, "obj", "rask", "spa-dev.pid");

        using var unrelated = Process.Start(new ProcessStartInfo("sleep", "60") { UseShellExecute = false })!;
        try
        {
            // Same pid, a different start time: what a record looks like once the pid belongs to someone else.
            Directory.CreateDirectory(Path.GetDirectoryName(pidFile)!);
            File.WriteAllText(pidFile, $"{unrelated.Id} 1");

            Assert.False(DevServerProcess.ReclaimOrphan(pidFile));
            Assert.False(unrelated.HasExited);
        }
        finally
        {
            unrelated.Kill();
        }
    }

    [Fact]
    public void No_record_ends_nothing()
    {
        using var project = new TempProject();

        Assert.False(DevServerProcess.ReclaimOrphan(Path.Combine(project.Path, "obj", "rask", "none.pid")));
    }

    [Fact]
    public async Task A_port_nobody_listens_on_is_not_mistaken_for_a_ready_server()
    {
        using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        Assert.False(await DevServerProcess.WaitForPortAsync("127.0.0.1", port, () => false, TimeSpan.FromMilliseconds(400), CancellationToken.None));
        Assert.False(await DevServerProcess.WaitForPortAsync("127.0.0.1", port, () => true, TimeSpan.FromSeconds(5), CancellationToken.None));
    }

    private sealed class TempProject : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("rask-devsession-").FullName;

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
