using System.Diagnostics;

namespace Rask.Meta.Hosting.Tests;

/// <summary>
///     A machine without Node gets RASKMETA003 — the message that says what to install — and nothing ahead of it.
/// </summary>
/// <remarks>
///     The probe's <c>Exec</c> lacked <c>IgnoreExitCode</c>, which the SPA and island probes carry: a missing
///     <c>node</c> made MSBuild report MSB3073 about the command itself, and under <c>-warnaserror</c> that is an
///     error before RASKMETA003 is ever read. Driven through the shipped build files and a real MSBuild, with a
///     <c>PATH</c> that holds no <c>node</c>.
/// </remarks>
public sealed class NodeProbeTests
{
    [Fact]
    public async Task A_machine_without_node_is_told_to_install_it_and_nothing_else()
    {
        // The system directories keep `sh`, which Exec runs the command through, and hold no Node where it is
        // installed the usual ways (a version manager, Homebrew, /usr/local).
        const string systemPath = "/usr/bin:/bin";
        Assert.SkipWhen(OperatingSystem.IsWindows(), "the probe is exercised through sh.");
        Assert.SkipWhen(File.Exists("/usr/bin/node") || File.Exists("/bin/node"), "node is on the system PATH itself.");

        var temp = Path.Combine(Path.GetTempPath(), "rask-meta-probe", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(temp, "client"));
        var build = Path.Combine(RepoRoot(), "src", "Rask.Meta.Hosting", "build");
        var project = Path.Combine(temp, "probe.proj");
        await File.WriteAllTextAsync(
            project,
            $"""
             <Project>
               <Import Project="{Path.Combine(build, "Rask.Meta.Hosting.props")}"/>
               <PropertyGroup><RaskMetaFramework>nextjs</RaskMetaFramework></PropertyGroup>
               <Import Project="{Path.Combine(build, "Rask.Meta.Hosting.targets")}"/>
             </Project>
             """,
            TestContext.Current.CancellationToken);

        try
        {
            var (exit, output) = await RunWithoutNode(systemPath, $"msbuild \"{project}\" -t:_RaskMetaProbeNode -nologo -warnaserror");

            Assert.NotEqual(0, exit);
            Assert.Contains("RASKMETA003", output, StringComparison.Ordinal);
            Assert.DoesNotContain("MSB3073", output, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    private static async Task<(int Exit, string Output)> RunWithoutNode(string path, string arguments)
    {
        var dotnet = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet";
        var start = new ProcessStartInfo(dotnet, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.Environment["PATH"] = path;

        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);

        return (process.ExitCode, await stdout + await stderr);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Rask.slnx")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
