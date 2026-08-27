using System.Diagnostics;
using System.Xml.Linq;

namespace Rask.TypeScript.Tasks.Tests;

/// <summary>
///     Type-checks every scoped <c>.ts</c> in the repository, under <c>--strict</c>.
/// </summary>
/// <remarks>
///     <para>
///         This is the half of the toolchain the ordinary build deliberately does not pay for. The
///         compile step in <c>Rask.Core.targets</c> runs tsgo with <c>noCheck</c>, because charging
///         every <c>dotnet build</c> for a full type check would tax the inner loop for a verdict
///         that belongs in a gate. Without this test that verdict would never be reached at all, and
///         the migration would have delivered TypeScript's syntax and none of its guarantee.
///     </para>
///     <para>
///         It runs per project, matching how the build compiles: each project's scoped files see
///         their own <c>.d.ts</c> siblings and the framework's <c>rask-globals.d.ts</c>, and nothing
///         from a project next door. Checking everything in one pass would let a declaration in one
///         app silently satisfy another app's code, and pass here while failing for a consumer.
///     </para>
/// </remarks>
public class ScopedTypeScriptTypeCheckTests
{
    /// <summary>Mirrors the Exclude list on the glob in Rask.Core.targets.</summary>
    private static readonly string[] ExcludedDirectories =
        ["bin", "obj", "node_modules", "wwwroot", "Resources", "Browser"];

    [Fact]
    public void EveryScopedTypeScriptFile_TypeChecks()
    {
        var root = RepositoryRoot();
        var tsgo = ResolveTsgo();
        var globals = Path.Combine(root, "src", "Rask.Core", "build", "rask-globals.d.ts");

        Assert.True(File.Exists(globals), $"the framework's ambient declarations are missing at '{globals}'");

        var projects = ProjectsWithScopedTypeScript(root).ToList();

        // A discovery bug would otherwise make this pass by checking nothing at all — the classic
        // shape of a gate that stops running without anyone noticing.
        Assert.NotEmpty(projects);

        var failures = new List<string>();
        foreach (var (project, files) in projects)
        {
            var arguments = string.Join(" ", files.Concat([globals]).Select(f => $"\"{f}\""))
                            + " --noEmit --strict --target es2020 --module esnext --lib es2020,dom";

            var (exitCode, output) = Run(tsgo, arguments);
            if (exitCode != 0)
            {
                failures.Add($"{Path.GetFileName(project)}:{Environment.NewLine}{output}");
            }
        }

        Assert.True(
            failures.Count == 0,
            "Scoped TypeScript did not type-check:" + Environment.NewLine + string.Join(Environment.NewLine, failures));
    }

    /// <summary>
    ///     Type-checks the framework's own service workers, which the scoped scan cannot reach.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <c>Resources/</c> is excluded from the scoped glob — those files are Rask's own runtime,
    ///         not a consumer's component assets — so without this they would be the one body of
    ///         TypeScript in the repository that nothing checks. The framework holding itself to a
    ///         lower standard than it holds its users to is the exact failure this migration exists to
    ///         remove.
    ///     </para>
    ///     <para>
    ///         A separate pass because the LIB is different: a service worker runs in a
    ///         ServiceWorkerGlobalScope, so it needs <c>webworker</c> where a component's scoped file
    ///         needs <c>dom</c>. The two cannot be checked in one compilation — their globals conflict.
    ///     </para>
    /// </remarks>
    [Fact]
    public void TheFrameworksServiceWorkers_TypeCheck()
    {
        var root = RepositoryRoot();
        var tsgo = ResolveTsgo();

        string[] workers =
        [
            Path.Combine(root, "src", "Rask.Core", "Resources", "rask-sw-shared.ts"),
            Path.Combine(root, "src", "Rask.Server", "Resources", "rask-sw.ts"),
            Path.Combine(root, "src", "Rask.Wasm", "Resources", "rask-sw.ts"),
        ];

        foreach (var worker in workers)
        {
            Assert.True(File.Exists(worker), $"'{worker}' is missing — the list here has gone stale");
        }

        var arguments = string.Join(" ", workers.Select(w => $"\"{w}\""))
                        + " --noEmit --strict --target es2020 --module esnext --moduleResolution bundler"
                        + " --lib es2020,webworker";

        var (exitCode, output) = Run(tsgo, arguments);

        Assert.True(exitCode == 0, "The framework's service workers did not type-check:" + Environment.NewLine + output);
    }

    /// <summary>
    ///     Every project directory holding at least one scoped <c>.ts</c>, with its files.
    /// </summary>
    /// <remarks>
    ///     Projects that opt out with <c>RaskScopedTsAutoInclude=false</c> are skipped, because their
    ///     <c>.ts</c> is not a scoped asset at all — <c>Rask.Spa.Hosting</c>'s client is vendored into
    ///     a consumer's bundler project and compiled by their toolchain, against their dependencies,
    ///     which are not present here.
    /// </remarks>
    private static IEnumerable<(string Project, List<string> Files)> ProjectsWithScopedTypeScript(string root)
    {
        foreach (var directory in new[] { "src", "samples", "tests" })
        {
            var path = Path.Combine(root, directory);
            if (!Directory.Exists(path))
            {
                continue;
            }

            foreach (var project in Directory.EnumerateFiles(path, "*.csproj", SearchOption.AllDirectories))
            {
                if (OptsOut(project))
                {
                    continue;
                }

                var projectDirectory = Path.GetDirectoryName(project)!;
                var files = Directory
                    .EnumerateFiles(projectDirectory, "*.ts", SearchOption.AllDirectories)
                    .Where(f => !IsExcluded(projectDirectory, f))
                    .OrderBy(f => f, StringComparer.Ordinal)
                    .ToList();

                // A project holding only .d.ts files has nothing to check: declarations describe
                // something else's code, and on their own they prove nothing.
                if (files.Any(f => !f.EndsWith(".d.ts", StringComparison.OrdinalIgnoreCase)))
                {
                    yield return (project, files);
                }
            }
        }
    }

    private static bool OptsOut(string project) =>
        XDocument.Load(project)
            .Descendants()
            .Any(e => e.Name.LocalName == "RaskScopedTsAutoInclude"
                      && string.Equals(e.Value.Trim(), "false", StringComparison.OrdinalIgnoreCase));

    private static bool IsExcluded(string projectDirectory, string file)
    {
        var relative = file.Substring(projectDirectory.Length)
            .Replace('\\', '/')
            .TrimStart('/');

        return relative.Split('/')
            .Any(segment => ExcludedDirectories.Contains(segment, StringComparer.OrdinalIgnoreCase));
    }

    private static string ResolveTsgo()
    {
        var engine = new SilentBuildEngine();
        var targets = XDocument.Load(
            Path.Combine(RepositoryRoot(), "src", "Rask.Core", "build", "Rask.Core.targets"));

        var task = new ResolveTypeScriptToolTask
        {
            BuildEngine = engine,
            Tool = "tsgo",
            Version = targets.Descendants().Single(e => e.Name.LocalName == "RaskTsgoVersion").Value,
            CacheRoot = TypeScriptTools.DefaultCacheRoot(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)),
        };

        Assert.True(task.Execute(), $"could not resolve tsgo: {string.Join("; ", engine.Errors)}");
        return task.ToolPath;
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Rask.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory!.FullName;
    }

    private static (int ExitCode, string Output) Run(string executable, string arguments)
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
    private sealed class SilentBuildEngine : Microsoft.Build.Framework.IBuildEngine
    {
        public List<string> Errors { get; } = [];

        public bool ContinueOnError => false;

        public int LineNumberOfTaskNode => 0;

        public int ColumnNumberOfTaskNode => 0;

        public string ProjectFileOfTaskNode => "test.csproj";

        public void LogErrorEvent(Microsoft.Build.Framework.BuildErrorEventArgs e) =>
            Errors.Add(e.Message ?? string.Empty);

        public void LogWarningEvent(Microsoft.Build.Framework.BuildWarningEventArgs e)
        {
        }

        public void LogMessageEvent(Microsoft.Build.Framework.BuildMessageEventArgs e)
        {
        }

        public void LogCustomEvent(Microsoft.Build.Framework.CustomBuildEventArgs e)
        {
        }

        public bool BuildProjectFile(
            string projectFileName,
            string[] targetNames,
            System.Collections.IDictionary globalProperties,
            System.Collections.IDictionary targetOutputs) => false;
    }
}
