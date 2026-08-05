namespace Rask.Cli.Tests;

/// <summary>
///     Every web-host sample must set a shutdown budget that fits the deploy window.
///     <para>
///         This is a repo-scanning test rather than a per-sample one because the failure mode is the
///         <em>next</em> sample somebody adds. Before this, nine of the ten web hosts inherited .NET's
///         default 30s <c>ShutdownTimeout</c> — which <b>exceeds</b> the 20s <c>rask deploy</c> allows
///         between SIGTERM and SIGKILL, so a sample deployed as written would be killed mid-shutdown.
///         Only <c>Rask.Example.Shop</c> had it right, and a reader copying from any other sample
///         inherited the wrong lesson.
///     </para>
/// </summary>
public class SamplesShutdownBudgetTests
{
    [Fact]
    public void Every_web_host_sample_budgets_its_shutdown_and_stops_services_concurrently()
    {
        var samples = Directory
            .EnumerateFiles(Path.Combine(RepoRoot(), "samples"), "Program.cs", SearchOption.AllDirectories)
            .Select(path => (Path: path, Text: File.ReadAllText(path)))
            // Only the web hosts: a console/WASM entry point has no HostOptions to configure.
            .Where(f => f.Text.Contains("WebApplication.CreateBuilder", StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(samples);

        var missing = samples
            .Where(f => !f.Text.Contains("ShutdownTimeout", StringComparison.Ordinal)
                        || !f.Text.Contains("ServicesStopConcurrently", StringComparison.Ordinal))
            .Select(f => Path.GetFileName(Path.GetDirectoryName(f.Path)))
            .ToList();

        Assert.True(missing.Count == 0,
            "these web-host samples inherit .NET's 30s default, which exceeds the 20s `rask deploy` allows "
            + "before SIGKILL: " + string.Join(", ", missing));
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Rask.slnx")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return dir.FullName;
    }
}
