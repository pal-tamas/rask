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
    ///     Type-checks the shipped browser layer AS A CONSUMER SEES IT — lib.dom and nothing else.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         These modules are packed into <c>Rask.Spa.Hosting</c> and copied into a TypeScript front
    ///         end's <c>src/rask/browser/</c>, where the only types available are the ones the browser's
    ///         own lib provides. The framework compiles them alongside <c>rask-window.d.ts</c>, which
    ///         declares every vendor shape it needs — <c>BatteryManagerLike</c>, <c>EyeDropper</c>,
    ///         <c>navigator.getBattery</c>, the speech-recognition constructors — so a module leaning on
    ///         one of those type-checks perfectly HERE and fails in the consumer's build.
    ///     </para>
    ///     <para>
    ///         Which is exactly what happened, and it is worth recording how it was caught. Nothing in
    ///         the unit gate saw it; the framework check was green, the packaging test was green, and
    ///         the failure surfaced at the last possible moment as <c>npm run build</c> exiting 2 inside
    ///         the CLI build gate, refusing a push. Eight modules were affected. This test is the
    ///         cheap version of that discovery: same compiler, no DOM shims, milliseconds.
    ///     </para>
    ///     <para>
    ///         <c>globals.ts</c> is excluded because it is the framework's own entry point rather than
    ///         part of what ships — it publishes the <c>window.__rask*</c> namespaces .NET resolves
    ///         against, and it is the one file in the directory a front end never receives.
    ///     </para>
    /// </remarks>
    [Fact]
    public void TheShippedBrowserModules_TypeCheckWithNothingButLibDom()
    {
        var root = RepositoryRoot();
        var tsgo = ResolveTsgo();

        var directory = Path.Combine(root, "src", "Rask.Core", "Resources", "browser");
        var modules = Directory.EnumerateFiles(directory, "*.ts")
            .Where(f => Path.GetFileName(f) != "globals.ts")
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();

        Assert.True(modules.Count > 1, $"No browser modules under '{directory}' — this checks nothing.");

        // Deliberately NOT passing rask-window.d.ts. That absence is the whole assertion.
        //
        // Checked against the STRICTEST consumer rather than an average one. `ng new` writes
        // noPropertyAccessFromIndexSignature, noImplicitOverride, noImplicitReturns and
        // noFallthroughCasesInSwitch; every other scaffolded client is looser. A module that compiles
        // under the loose set and not the strict one is not "mostly fine" — it is broken for one of the
        // seven frameworks `rask new` offers, and it surfaces as `npm run build` exiting 1 with nothing
        // pointing back at the line. That is exactly how deviceMotion.ts's dot-access into an index
        // signature was found: by the CLI gate, on a third rejected push, minutes at a time.
        var arguments = string.Join(" ", modules.Select(m => $"\"{m}\""))
                        + " --noEmit --strict --noUnusedLocals --noPropertyAccessFromIndexSignature"
                        + " --noImplicitOverride --noImplicitReturns --noFallthroughCasesInSwitch"
                        + " --isolatedModules --target es2022 --module esnext"
                        + " --moduleResolution bundler --lib es2022,dom";

        var (exitCode, output) = Run(tsgo, arguments);

        Assert.True(
            exitCode == 0,
            "The shipped browser modules do not compile against lib.dom alone, so a scaffolded "
            + "TypeScript client will not build. Declare the vendor shape inside the module that needs "
            + "it rather than relying on rask-window.d.ts, which never leaves the framework:"
            + Environment.NewLine + output);
    }

    /// <summary>
    ///     Type-checks the framework's own client runtimes — the diff codec, the morph, the event
    ///     router, the browser-API shims and both host entry points.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         These live under <c>Resources/</c> and <c>Browser/</c>, both excluded from the scoped
    ///         glob, so nothing else in the repository checks them. They are also the largest and
    ///         most load-bearing TypeScript here: a wrong type in the diff codec is a corrupted DOM.
    ///     </para>
    ///     <para>
    ///         <b><c>noUnusedLocals</c> is not style enforcement here.</b> esbuild drops a module
    ///         whose every import went unreferenced — TypeScript elides the unused import, esbuild
    ///         then judges the module unreachable and removes it along with its top-level side
    ///         effects. That is how the WASM bundle silently lost the hot-reload indicator, and how
    ///         it would have shipped without <c>setHost</c> ever being called. An unused import in
    ///         these files is not untidy, it is a module that will not be in the bundle.
    ///     </para>
    ///     <para>
    ///         One pass, not one per file: they import each other, and checking a file alone would
    ///         resolve its imports against sources nobody verified in the same compilation.
    ///     </para>
    /// </remarks>
    [Fact]
    public void TheFrameworksClientRuntimes_TypeCheck()
    {
        var root = RepositoryRoot();
        var tsgo = ResolveTsgo();

        // The ambient declaration files, which are inputs to the check but never its subjects.
        string[] declarations =
        [
            Path.Combine(root, "src", "Rask.Core", "build", "rask-globals.d.ts"),
            Path.Combine(root, "src", "Rask.Core", "Resources", "rask-window.d.ts"),
            Path.Combine(root, "src", "Rask.Wasm", "Resources", "rask-wasm-window.d.ts"),
            Path.Combine(root, "src", "Rask.Wasm", "Browser", "rask.wasm.d.ts"),
        ];

        foreach (var declaration in declarations)
        {
            Assert.True(File.Exists(declaration), $"'{declaration}' is missing — the list here has gone stale");
        }

        // DERIVED, not listed. This used to be a hand-written list of every runtime file, guarded by a
        // staleness check that enumerated TopDirectoryOnly — so a file in a SUBDIRECTORY (Resources/
        // browser/, once the browser layer was extracted into modules) was in neither the list nor the
        // guard, and went unchecked with the gate still green. Enumerating is what makes that
        // unrepresentable: a file cannot be added to these trees without this test compiling it.
        var runtimes = new[]
            {
                Path.Combine(root, "src", "Rask.Core", "Resources"),
                Path.Combine(root, "src", "Rask.Server", "Resources"),
                Path.Combine(root, "src", "Rask.Wasm", "Resources"),
                Path.Combine(root, "src", "Rask.Wasm", "Browser"),
            }
            .SelectMany(d => Directory.EnumerateFiles(d, "*.ts", SearchOption.AllDirectories))
            .Where(f => !f.EndsWith(".d.ts", StringComparison.Ordinal))
            // The service workers are checked by TheFrameworksServiceWorkers_TypeCheck, against the
            // webworker lib rather than dom.
            .Where(f => !Path.GetFileName(f).StartsWith("rask-sw", StringComparison.Ordinal))
            .Select(Path.GetFullPath)
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();

        Assert.NotEmpty(runtimes);

        string[] sources = [.. declarations.Select(Path.GetFullPath), .. runtimes];

        var arguments = string.Join(" ", sources.Select(f => $"\"{f}\""))
                        + " --noEmit --strict --noUnusedLocals --target es2020 --module esnext"
                        + " --moduleResolution bundler --lib es2020,dom";

        var (exitCode, output) = Run(tsgo, arguments);

        Assert.True(
            exitCode == 0,
            "The framework's client runtimes did not type-check:" + Environment.NewLine + output);
    }

    /// <summary>
    ///     No compiled <c>.js</c> sits beside a framework <c>.ts</c>.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A TypeScript compiler run without <c>noEmit</c> and without an <c>outDir</c> writes its
    ///         output next to the source. One did, and left a <c>rask-morph.js</c> in
    ///         <c>Rask.Core/Resources</c> that was staged for commit before anyone noticed.
    ///     </para>
    ///     <para>
    ///         It is worth a test rather than a <c>.gitignore</c> line because of what it would do if
    ///         it shipped: esbuild resolves <c>"./rask-morph.js"</c> — the extension every import here
    ///         writes — and a real file by that name wins over the TypeScript it was meant to resolve
    ///         to. The bundle would then be built from a stale compile of the module, silently, with
    ///         the source right beside it looking correct.
    ///     </para>
    /// </remarks>
    [Fact]
    public void NoCompiledJavaScriptSitsBesideTheFrameworkTypeScript()
    {
        var root = RepositoryRoot();

        string[] directories =
        [
            Path.Combine(root, "src", "Rask.Core", "Resources"),
            Path.Combine(root, "src", "Rask.Core", "build"),
            Path.Combine(root, "src", "Rask.Server", "Resources"),
            Path.Combine(root, "src", "Rask.Wasm", "Resources"),
            Path.Combine(root, "tests", "Rask.Core.Tests", "Live"),
        ];

        var strays = new List<string>();
        foreach (var directory in directories)
        {
            Assert.True(Directory.Exists(directory), $"'{directory}' is missing — the list here has gone stale");

            foreach (var javaScript in Directory.EnumerateFiles(directory, "*.js", SearchOption.TopDirectoryOnly))
            {
                var sibling = Path.ChangeExtension(javaScript, ".ts");
                if (File.Exists(sibling))
                {
                    strays.Add(javaScript);
                }
            }
        }

        Assert.True(
            strays.Count == 0,
            "A compiled .js is sitting beside its TypeScript source. Delete it — esbuild would resolve "
            + "the import to it instead of the .ts, and bundle a stale compile:"
            + Environment.NewLine + string.Join(Environment.NewLine, strays));
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
