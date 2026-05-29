using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Rask.Core.ScopedAssets;
using Rask.Wasm.Tasks;

namespace Rask.Wasm.Tasks.Tests;

/// <summary>
///     Verifies <see cref="BakeScopedAssetsTask" /> writes the scoped-asset registry
///     to <c>{BundleDir}/_rask/a/{hash}.{ext}</c> for standalone WASM deploys (GH
///     Pages, plain static-file servers). Covers the early-return paths and the
///     end-to-end bake driven against a real assembly (Rask.Example.Shared) whose
///     <c>__RaskScopedCssRegistration</c> module initializer fires on test startup.
/// </summary>
public sealed class BakeScopedAssetsTaskTests : IDisposable
{
    private readonly string _bundleDir;

    public BakeScopedAssetsTaskTests()
    {
        _bundleDir = Path.Combine(Path.GetTempPath(),
            "rask-wasm-tasks-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_bundleDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_bundleDir, recursive: true); }
        catch { /* best effort */ }
    }

    private static BakeScopedAssetsTask NewTask(string bundleDir, ITaskItem[] assemblies)
        => new()
        {
            BundleDir = bundleDir,
            Assemblies = assemblies,
            BuildEngine = new StubBuildEngine()
        };

    [Fact]
    public void EmptyBundleDir_ReturnsTrue_NoOutput()
    {
        var task = NewTask("", Array.Empty<ITaskItem>());

        Assert.True(task.Execute());
    }

    [Fact]
    public void MissingBundleDir_ReturnsTrue_NoOutput()
    {
        var task = NewTask(Path.Combine(_bundleDir, "definitely-not-here"),
            Array.Empty<ITaskItem>());

        Assert.True(task.Execute());
        Assert.False(Directory.Exists(Path.Combine(_bundleDir, "definitely-not-here")));
    }

    [Fact]
    public void NoAssemblies_ReturnsTrue_DoesNotCreateRaskFolder()
    {
        var task = NewTask(_bundleDir, Array.Empty<ITaskItem>());

        Assert.True(task.Execute());
        Assert.False(Directory.Exists(Path.Combine(_bundleDir, "_rask", "a")));
    }

    [Fact]
    public void AssembliesPointToNothing_ReturnsTrue_NoFilesWritten()
    {
        var task = NewTask(_bundleDir, new ITaskItem[]
        {
            new TaskItem(Path.Combine(_bundleDir, "does-not-exist.dll"))
        });

        Assert.True(task.Execute());
        // _rask/a/ may or may not get created (registry still scanned); critical is no .css/.js files.
        var outDir = Path.Combine(_bundleDir, "_rask", "a");
        if (Directory.Exists(outDir))
        {
            Assert.Empty(Directory.EnumerateFiles(outDir));
        }
    }

    [Fact]
    public void RealAssemblies_BakeWritesOneFilePerRegisteredAsset()
    {
        // Rask.Example.Shared's source-generator-emitted __RaskScopedCssRegistration and
        // __RaskScopedJsRegistration run at assembly load; this test's project references
        // Rask.Example.Shared so by the time the test executes, the in-process registry
        // already contains its entries. The task RefreshAll-loops over those registration
        // classes, then writes one file per (hash, kind) entry.
        var raskCoreDll = typeof(ScopedAssetRegistry).Assembly.Location;
        var exampleSharedDll = typeof(Rask.Example.Shared.App).Assembly.Location;
        Assert.True(File.Exists(raskCoreDll), $"Rask.Core.dll not at {raskCoreDll}");
        Assert.True(File.Exists(exampleSharedDll), $"Rask.Example.Shared.dll not at {exampleSharedDll}");

        var task = NewTask(_bundleDir, new ITaskItem[]
        {
            new TaskItem(raskCoreDll),
            new TaskItem(exampleSharedDll),
        });

        Assert.True(task.Execute());

        var outDir = Path.Combine(_bundleDir, "_rask", "a");
        Assert.True(Directory.Exists(outDir), "_rask/a directory should exist after a successful bake");

        var registeredCount = ScopedAssetRegistry.EnumerateAll().Count();
        Assert.True(registeredCount > 0, "Rask.Example.Shared should register at least one scoped asset");

        var bakedFiles = Directory.EnumerateFiles(outDir).ToArray();
        Assert.Equal(registeredCount, bakedFiles.Length);
    }

    [Fact]
    public void RealAssemblies_BakedFilenamesMatchHashAndExtension()
    {
        var raskCoreDll = typeof(ScopedAssetRegistry).Assembly.Location;
        var exampleSharedDll = typeof(Rask.Example.Shared.App).Assembly.Location;

        var task = NewTask(_bundleDir, new ITaskItem[]
        {
            new TaskItem(raskCoreDll),
            new TaskItem(exampleSharedDll),
        });
        task.Execute();

        var outDir = Path.Combine(_bundleDir, "_rask", "a");
        var entries = ScopedAssetRegistry.EnumerateAll().ToList();
        foreach (var entry in entries)
        {
            var ext = entry.Kind == AssetKind.Css ? "css" : "js";
            var expected = Path.Combine(outDir, entry.Hash + "." + ext);
            Assert.True(File.Exists(expected), $"missing baked file at {expected}");
            var written = File.ReadAllBytes(expected);
            Assert.Equal(entry.Utf8.ToArray(), written);
        }
    }

    [Fact]
    public void Rerun_OverwritesSameFiles_Idempotent()
    {
        var raskCoreDll = typeof(ScopedAssetRegistry).Assembly.Location;
        var exampleSharedDll = typeof(Rask.Example.Shared.App).Assembly.Location;
        var assemblies = new ITaskItem[]
        {
            new TaskItem(raskCoreDll),
            new TaskItem(exampleSharedDll),
        };

        NewTask(_bundleDir, assemblies).Execute();
        var outDir = Path.Combine(_bundleDir, "_rask", "a");
        var first = Directory.EnumerateFiles(outDir).OrderBy(p => p).ToArray();

        NewTask(_bundleDir, assemblies).Execute();
        var second = Directory.EnumerateFiles(outDir).OrderBy(p => p).ToArray();

        Assert.Equal(first, second);
    }

    /// <summary>
    ///     Minimal IBuildEngine stub for task tests. The bake task only calls Log.*; all
    ///     messages are captured into <see cref="Messages" /> for assertions that care
    ///     (none currently do, but cheap to keep).
    /// </summary>
    private sealed class StubBuildEngine : IBuildEngine
    {
        public List<string> Messages { get; } = new();
        public List<string> Warnings { get; } = new();
        public List<string> Errors { get; } = new();

        public bool ContinueOnError => false;
        public int LineNumberOfTaskNode => 0;
        public int ColumnNumberOfTaskNode => 0;
        public string ProjectFileOfTaskNode => string.Empty;

        public void LogErrorEvent(BuildErrorEventArgs e) => Errors.Add(e.Message ?? "");
        public void LogWarningEvent(BuildWarningEventArgs e) => Warnings.Add(e.Message ?? "");
        public void LogMessageEvent(BuildMessageEventArgs e) => Messages.Add(e.Message ?? "");
        public void LogCustomEvent(CustomBuildEventArgs e) { }

        public bool BuildProjectFile(
            string projectFileName,
            string[] targetNames,
            System.Collections.IDictionary globalProperties,
            System.Collections.IDictionary targetOutputs) => false;
    }
}
