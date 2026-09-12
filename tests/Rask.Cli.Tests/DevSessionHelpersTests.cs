using System.Diagnostics;
using Rask.Hosting.Shared;

namespace Rask.Cli.Tests;

/// <summary>
///     The dev-session helpers <c>rask dev</c> and an editor-launched app share: the islands pointer, the
///     dev script, and the dev-server process that has to survive a debugger's hard stop without leaving a
///     port taken.
/// </summary>
public sealed class DevSessionHelpersTests
{
    // ---- islands pointer ----

    [Fact]
    public void A_pointer_naming_a_real_config_is_read()
    {
        using var project = new TempProject();
        var config = project.Write("obj/rask-external/vite.config.mjs", "export default {}");
        project.Write("obj/rask-external/dev.json", $$"""{ "url": "http://localhost:5174", "config": "{{config.Replace('\\', '/')}}" }""");

        var pointer = IslandDevPointer.TryRead(project.Path);

        Assert.NotNull(pointer);
        Assert.Equal("http://localhost:5174", pointer!.Value.Url);
    }

    [Theory]
    [InlineData("""{ "url": "http://localhost:5174", "config": "/no/such/vite.config.mjs" }""")] // config missing
    [InlineData("""{ "url": "http://localhost:5174", """)]                                     // half-written
    [InlineData("""{ "url": 5174, "config": "x" }""")]                                           // wrong shape
    [InlineData("""{ "config": "x" }""")]                                                        // older writer
    public void A_pointer_that_cannot_be_used_yet_reads_as_none(string json)
    {
        using var project = new TempProject();
        project.Write("obj/rask-external/dev.json", json);

        Assert.Null(IslandDevPointer.TryRead(project.Path));
    }

    [Fact]
    public async Task Waiting_gives_up_quietly()
    {
        using var project = new TempProject();

        Assert.Null(await IslandDevPointer.WaitAsync(project.Path, TimeSpan.FromMilliseconds(300), CancellationToken.None));
    }

    // ---- dev script ----

    [Theory]
    [InlineData("""{ "scripts": { "dev": "vite", "start": "x" } }""", "dev")]
    [InlineData("""{ "scripts": { "start": "ng serve" } }""", "start")]   // the Angular CLI's name
    [InlineData("""{ "scripts": { "build": "vite build" } }""", "dev")]
    [InlineData("""{ "scripts": "not an object" }""", "dev")]
    [InlineData("""{ not json""", "dev")]
    [InlineData(null, "dev")]
    public void The_dev_script_is_the_one_the_manifest_names(string? manifest, string expected)
    {
        Assert.Equal(expected, DevScript.FromManifest(manifest));
    }

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

        public string Write(string relative, string content)
        {
            var full = System.IO.Path.Combine(Path, relative.Replace('/', System.IO.Path.DirectorySeparatorChar));
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
            File.WriteAllText(full, content);
            return full;
        }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
