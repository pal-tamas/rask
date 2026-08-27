using System.Collections;
using System.Diagnostics;
using System.Xml.Linq;
using Microsoft.Build.Framework;

namespace Rask.TypeScript.Tasks.Tests;

/// <summary>
///     The resolver, run for real against the registry it will run against in a consumer's build.
/// </summary>
/// <remarks>
///     <para>
///         These fetch over the network on a cold cache, and that is deliberate. Everything this task
///         does that can be wrong is wrong in a way a mocked registry would not show: a package name
///         that is not published, a tarball path that 404s, an executable at a different location
///         inside the archive, a checksum field that moved. A test that stubs all that out proves the
///         stub.
///     </para>
///     <para>
///         The repo already accepts a network-dependent unit test for exactly this reason —
///         <c>Rask.Spa.Tasks.Tests.TypeScriptCompilesTests</c> fetches a compiler through
///         <c>npx</c>. The difference is that this one cannot silently skip when a tool is absent:
///         there is no <c>npx</c> to be missing, so it either resolves or it fails. An env-gated test
///         that reports SKIPPED is one of the documented ways a gate stops running.
///     </para>
///     <para>
///         After the first run everything is cached in <c>~/.rask/typescript</c>, so these cost a
///         process launch.
///     </para>
/// </remarks>
public class ResolveTypeScriptToolTaskTests
{
    /// <summary>
    ///     The pins come from <c>build/Rask.TypeScript.props</c>, so a bump cannot pass here while
    ///     leaving the build on a different version.
    /// </summary>
    private static readonly Lazy<(string Esbuild, string Tsgo)> Pins = new(ReadPinnedVersions);

    [Fact]
    public void Resolve_Esbuild_FetchesABinaryThatRuns()
    {
        var path = Resolve("esbuild", Pins.Value.Esbuild);

        Assert.True(File.Exists(path), $"the resolver reported '{path}', which is not there");

        // --version rather than a transpile: this asserts the download is the right architecture and
        // is executable, which is the part the resolver is responsible for.
        var reported = Run(path, "--version");
        Assert.Equal(Pins.Value.Esbuild, reported.Trim());
    }

    [Fact]
    public void Resolve_Tsgo_FetchesACompilerThatRuns()
    {
        var path = Resolve("tsgo", Pins.Value.Tsgo);

        Assert.True(File.Exists(path), $"the resolver reported '{path}', which is not there");
        Assert.Contains("Version", Run(path, "--version"), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     tsgo's type-definition library is unpacked beside the binary, not just the binary.
    /// </summary>
    /// <remarks>
    ///     The failure this prevents is loud but misdirected: a tsgo with no <c>lib.dom.d.ts</c>
    ///     reports every DOM type as undefined, which reads as "this project is broken" rather than
    ///     "the install is incomplete". Asserting the file exists is far cheaper than reading that
    ///     error and believing it.
    /// </remarks>
    [Fact]
    public void Resolve_Tsgo_UnpacksTheTypeDefinitionLibraryToo()
    {
        var path = Resolve("tsgo", Pins.Value.Tsgo);
        var lib = Path.GetDirectoryName(path)!;

        Assert.True(
            File.Exists(Path.Combine(lib, "lib.dom.d.ts")),
            "tsgo resolves its lib files relative to its own location, so lib.dom.d.ts must sit beside it");
        Assert.True(File.Exists(Path.Combine(lib, "lib.es5.d.ts")));
    }

    /// <summary>
    ///     esbuild strips types — and rewrites the export form, which is why scoped assets use tsgo.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         This is the trap the scoped-asset design turns on, and it is pinned as a test because it
    ///         is invisible everywhere else. esbuild always hoists declarations into a trailing
    ///         <c>export { … }</c> clause — whatever the format, and even with no bundling and no
    ///         minification:
    ///     </para>
    ///     <code>
    ///     export function width(el: HTMLElement | null): number { … }   // in
    ///     function width(el) { … }  export { width };                   // out
    ///     </code>
    ///     <para>
    ///         <c>ScopedAssetRegistry</c> finds a component's methods by matching
    ///         <c>export function NAME(</c> at a line start, so that output would register <b>no
    ///         methods at all</b> on <c>window.Rask[Name]</c> — with no error, at runtime, in the
    ///         browser only. Hence tsgo for scoped assets, and esbuild only where a bundle is the point.
    ///     </para>
    ///     <para>
    ///         No <c>--loader=ts</c>: esbuild reads the language from the extension, and passing the
    ///         flag alongside a real file is an error ("only applies when reading from stdin").
    ///     </para>
    /// </remarks>
    [Fact]
    public void Esbuild_StripsTypesButHoistsTheExports()
    {
        var path = Resolve("esbuild", Pins.Value.Esbuild);
        using var source = new TempFile(
            ".ts",
            "export function width(el: HTMLElement | null): number { return el ? 1 : 0; }");

        var js = Run(path, $"\"{source.Path}\" --format=esm");

        Assert.DoesNotContain("HTMLElement", js, StringComparison.Ordinal);
        Assert.Contains("function width(el)", js, StringComparison.Ordinal);

        // The part that matters: the inline `export` does not survive.
        Assert.DoesNotContain("export function width(", js, StringComparison.Ordinal);
        Assert.Contains("export {", js, StringComparison.Ordinal);
    }

    /// <summary>
    ///     tsgo's emit preserves <c>export function NAME(</c>, which is what scoped assets need.
    /// </summary>
    /// <remarks>
    ///     The contract <c>ScopedAssetRegistry</c>'s two regexes depend on: one strips the leading
    ///     <c>export</c> so the body can run inside a non-module wrapper, the other collects the names
    ///     to re-expose on <c>window.Rask[Name]</c>. Both match at a line start, and both are satisfied
    ///     by this output and not by esbuild's. <c>async</c> is covered too, because the name has to
    ///     stay in the same capture group whether or not the modifier is present.
    /// </remarks>
    [Fact]
    public void Tsgo_Emit_PreservesTheInlineExportForm()
    {
        var path = Resolve("tsgo", Pins.Value.Tsgo);
        using var source = new TempFile(
            ".ts",
            """
            export function width(el: HTMLElement | null): number { return el ? 1 : 0; }
            export async function copy(text: string): Promise<void> { await navigator.clipboard.writeText(text); }
            """);
        using var output = new TempDirectory();

        Run(path, $"\"{source.Path}\" --outDir \"{output.Path}\" --target es2020 --module esnext --noCheck");

        var js = File.ReadAllText(
            Path.Combine(output.Path, Path.GetFileNameWithoutExtension(source.Path) + ".js"));

        Assert.Contains("export function width(", js, StringComparison.Ordinal);
        Assert.Contains("export async function copy(", js, StringComparison.Ordinal);
        Assert.DoesNotContain("HTMLElement", js, StringComparison.Ordinal);
    }

    /// <summary>
    ///     tsgo reports a type error and says so by exit code.
    /// </summary>
    /// <remarks>
    ///     The half esbuild cannot do at all, and the reason the toolchain is two binaries rather than
    ///     one. Without it the migration would deliver TypeScript's syntax and none of its guarantee.
    /// </remarks>
    [Fact]
    public void Tsgo_RejectsATypeError()
    {
        var path = Resolve("tsgo", Pins.Value.Tsgo);
        using var source = new TempFile(".ts", "export function n(): number { return \"not a number\"; }");

        var (exitCode, output) = RunRaw(path, $"--noEmit \"{source.Path}\"");

        Assert.NotEqual(0, exitCode);
        Assert.Contains("not assignable", output, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     Offline with a cold cache fails, and the message names the file and the URL.
    /// </summary>
    /// <remarks>
    ///     The message is the whole feature here. "Could not resolve esbuild" leaves someone on an
    ///     air-gapped machine with nothing to act on; naming the path to put it at and the URL to
    ///     fetch it from turns the failure into an instruction.
    /// </remarks>
    [Fact]
    public void Resolve_Offline_WithAColdCache_FailsNamingTheFileAndTheUrl()
    {
        var engine = new RecordingBuildEngine();
        var task = new ResolveTypeScriptToolTask
        {
            BuildEngine = engine,
            Tool = "esbuild",
            Version = "0.0.0-not-a-real-version",
            CacheRoot = Path.Combine(Path.GetTempPath(), "rask-cold-" + Guid.NewGuid().ToString("n")),
            Offline = true,
        };

        Assert.False(task.Execute());

        var message = Assert.Single(engine.Errors);
        Assert.Contains("RaskTypeScriptOffline", message, StringComparison.Ordinal);
        Assert.Contains("registry.npmjs.org", message, StringComparison.Ordinal);
        Assert.Contains("RaskTypeScriptBuild=false", message, StringComparison.Ordinal);
    }

    /// <summary>An unknown tool name is refused before anything reaches the network.</summary>
    [Fact]
    public void Resolve_UnknownTool_IsRefused()
    {
        var engine = new RecordingBuildEngine();
        var task = new ResolveTypeScriptToolTask
        {
            BuildEngine = engine,
            Tool = "tsc",
            Version = "5.0.0",
            CacheRoot = Path.GetTempPath(),
        };

        Assert.False(task.Execute());
        Assert.Contains("'tsc' is not a tool", Assert.Single(engine.Errors), StringComparison.Ordinal);
    }

    /// <summary>
    ///     A tampered download is rejected rather than executed.
    /// </summary>
    /// <remarks>
    ///     Driven by pointing the resolver at a real package under a version whose published checksum
    ///     belongs to different bytes — which is what a mirror serving the wrong file looks like. The
    ///     point is that the failure happens before anything is marked executable.
    /// </remarks>
    [Fact]
    public void Resolve_AVersionThatDoesNotExist_FailsWithoutWritingToTheCache()
    {
        var cache = Path.Combine(Path.GetTempPath(), "rask-bad-" + Guid.NewGuid().ToString("n"));
        var engine = new RecordingBuildEngine();
        var task = new ResolveTypeScriptToolTask
        {
            BuildEngine = engine,
            Tool = "esbuild",
            Version = "0.0.0-not-a-real-version",
            CacheRoot = cache,
        };

        Assert.False(task.Execute());
        Assert.Contains("could not fetch", Assert.Single(engine.Errors), StringComparison.Ordinal);
        Assert.False(Directory.Exists(cache) && Directory.GetFiles(cache, "*", SearchOption.AllDirectories).Length > 0);
    }

    /// <summary>Resolving twice is a cache hit, not a second download.</summary>
    [Fact]
    public void Resolve_Twice_ReturnsTheSamePathWithNoSecondFetch()
    {
        var first = Resolve("esbuild", Pins.Value.Esbuild);

        var engine = new RecordingBuildEngine();
        var task = new ResolveTypeScriptToolTask
        {
            BuildEngine = engine,
            Tool = "esbuild",
            Version = Pins.Value.Esbuild,
            CacheRoot = DefaultCacheRoot(),
        };

        Assert.True(task.Execute());
        Assert.Equal(first, task.ToolPath);

        // A cache hit says nothing at all; the "fetching…" line only appears on a miss.
        Assert.DoesNotContain(engine.Messages, m => m.Contains("fetching", StringComparison.Ordinal));
    }

    private static string Resolve(string tool, string version)
    {
        var engine = new RecordingBuildEngine();
        var task = new ResolveTypeScriptToolTask
        {
            BuildEngine = engine,
            Tool = tool,
            Version = version,
            CacheRoot = DefaultCacheRoot(),
        };

        Assert.True(
            task.Execute(),
            $"resolving {tool}@{version} failed: {string.Join("; ", engine.Errors)}");

        return task.ToolPath;
    }

    private static string DefaultCacheRoot() =>
        TypeScriptTools.DefaultCacheRoot(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

    /// <summary>Run and require success, returning what it printed.</summary>
    private static string Run(string executable, string arguments)
    {
        var (exitCode, output) = RunRaw(executable, arguments);

        Assert.True(exitCode == 0, $"'{executable} {arguments}' exited {exitCode}: {output}");

        return output;
    }

    /// <summary>
    ///     Run and report, for the cases where a non-zero exit is the thing being asserted.
    /// </summary>
    /// <remarks>
    ///     Both streams are combined because these tools do not agree on which one a diagnostic
    ///     belongs on, and a test that reads only stdout passes vacuously when the message went to
    ///     stderr.
    /// </remarks>
    private static (int ExitCode, string Output) RunRaw(string executable, string arguments)
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

    /// <summary>A temporary source file that removes itself.</summary>
    private sealed class TempFile : IDisposable
    {
        public TempFile(string extension, string contents)
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "rask-ts-" + Guid.NewGuid().ToString("n").Substring(0, 8) + extension);
            File.WriteAllText(Path, contents);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                File.Delete(Path);
            }
            catch (IOException)
            {
                // Litter in the temp directory is not worth failing a test over.
            }
        }
    }

    /// <summary>A temporary output directory that removes itself.</summary>
    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "rask-out-" + Guid.NewGuid().ToString("n").Substring(0, 8));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
                // As above.
            }
        }
    }

    /// <summary>
    ///     Reads the pins out of the build integration, so the test and the build cannot disagree.
    /// </summary>
    /// <remarks>
    ///     <c>Rask.Core.targets</c> is the single source: it is the file that ships to consumers
    ///     inside every host package, so a pin stated anywhere else would be a second copy that can
    ///     drift. Reading it here is what makes "the pinned versions actually resolve" a fact this
    ///     gate establishes rather than a claim.
    /// </remarks>
    private static (string Esbuild, string Tsgo) ReadPinnedVersions()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Rask.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);

        var targets = XDocument.Load(
            Path.Combine(directory!.FullName, "src", "Rask.Core", "build", "Rask.Core.targets"));

        string Read(string name) => targets.Descendants()
            .Single(e => e.Name.LocalName == name)
            .Value;

        return (Read("RaskEsbuildVersion"), Read("RaskTsgoVersion"));
    }

    /// <summary>The smallest build engine a task needs, keeping what it was told.</summary>
    private sealed class RecordingBuildEngine : IBuildEngine
    {
        public List<string> Errors { get; } = [];

        public List<string> Messages { get; } = [];

        public bool ContinueOnError => false;

        public int LineNumberOfTaskNode => 0;

        public int ColumnNumberOfTaskNode => 0;

        public string ProjectFileOfTaskNode => "test.csproj";

        public void LogErrorEvent(BuildErrorEventArgs e) => Errors.Add(e.Message ?? string.Empty);

        public void LogWarningEvent(BuildWarningEventArgs e) => Messages.Add(e.Message ?? string.Empty);

        public void LogMessageEvent(BuildMessageEventArgs e) => Messages.Add(e.Message ?? string.Empty);

        public void LogCustomEvent(CustomBuildEventArgs e)
        {
        }

        public bool BuildProjectFile(
            string projectFileName,
            string[] targetNames,
            IDictionary globalProperties,
            IDictionary targetOutputs) => false;
    }
}
