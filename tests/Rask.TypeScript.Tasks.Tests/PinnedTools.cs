using System.Diagnostics;
using System.Xml.Linq;
using Microsoft.Build.Framework;

namespace Rask.TypeScript.Tasks.Tests;

/// <summary>
///     The TypeScript tools at the versions the build pins, and the repository they run over — for tests that
///     check the framework's own client sources with the same compilers the build uses.
/// </summary>
/// <remarks>
///     <c>ResolveTypeScriptToolTaskTests</c> keeps its own resolution on purpose: it is testing the task, so it
///     drives it with a build engine that records every message rather than this one, which keeps only errors.
/// </remarks>
internal static class PinnedTools
{
    /// <summary>
    ///     Resolves <paramref name="tool" /> (<c>esbuild</c> or <c>tsgo</c>) at the version <c>Rask.Core.targets</c>
    ///     pins, from the shared per-user cache, fetching it on a miss.
    /// </summary>
    public static string Resolve(string tool)
    {
        var property = tool switch
        {
            "esbuild" => "RaskEsbuildVersion",
            "tsgo" => "RaskTsgoVersion",
            _ => throw new ArgumentOutOfRangeException(nameof(tool), tool, "Rask pins esbuild and tsgo only."),
        };

        var targets = XDocument.Load(Path.Combine(RepositoryRoot(), "src", "Rask.Core", "build", "Rask.Core.targets"));
        var engine = new SilentBuildEngine();
        var task = new ResolveTypeScriptToolTask
        {
            BuildEngine = engine,
            Tool = tool,
            Version = targets.Descendants().Single(e => e.Name.LocalName == property).Value,
            CacheRoot = TypeScriptTools.DefaultCacheRoot(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)),
        };

        Assert.True(task.Execute(), $"could not resolve {tool}: {string.Join("; ", engine.Errors)}");
        return task.ToolPath;
    }

    /// <summary>The directory holding <c>Rask.slnx</c>, found by walking up from the test assembly.</summary>
    public static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Rask.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory!.FullName;
    }

    /// <summary>
    ///     Runs <paramref name="executable" /> and returns its exit code with stdout and stderr combined, because
    ///     these tools do not agree on which stream a diagnostic belongs on.
    /// </summary>
    public static (int ExitCode, string Output) Run(string executable, string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo(executable, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        })!;

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return (process.ExitCode, stdout + stderr);
    }

    /// <summary>A build engine that keeps errors and discards the rest.</summary>
    private sealed class SilentBuildEngine : IBuildEngine
    {
        public List<string> Errors { get; } = [];

        public bool ContinueOnError => false;

        public int LineNumberOfTaskNode => 0;

        public int ColumnNumberOfTaskNode => 0;

        public string ProjectFileOfTaskNode => "test.csproj";

        public void LogErrorEvent(BuildErrorEventArgs e) => Errors.Add(e.Message ?? string.Empty);

        public void LogWarningEvent(BuildWarningEventArgs e)
        {
        }

        public void LogMessageEvent(BuildMessageEventArgs e)
        {
        }

        public void LogCustomEvent(CustomBuildEventArgs e)
        {
        }

        public bool BuildProjectFile(
            string projectFileName,
            string[] targetNames,
            System.Collections.IDictionary globalProperties,
            System.Collections.IDictionary targetOutputs) => false;
    }
}
