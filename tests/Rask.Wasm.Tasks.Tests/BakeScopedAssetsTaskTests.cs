using System.Collections;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Rask.Core.ScopedAssets;
using Rask.Example.Shared;

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
        try { Directory.Delete(_bundleDir, true); }
        catch
        {
            /* best effort */
        }
    }

    private static BakeScopedAssetsTask NewTask(string bundleDir, ITaskItem[] assemblies)
        => new() { BundleDir = bundleDir, Assemblies = assemblies, BuildEngine = new StubBuildEngine() };

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
        var task = NewTask(_bundleDir,
            new ITaskItem[] { new TaskItem(Path.Combine(_bundleDir, "does-not-exist.dll")) });

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
        var exampleSharedDll = typeof(App).Assembly.Location;
        Assert.True(File.Exists(raskCoreDll), $"Rask.Core.dll not at {raskCoreDll}");
        Assert.True(File.Exists(exampleSharedDll), $"Rask.Example.Shared.dll not at {exampleSharedDll}");

        var task = NewTask(_bundleDir, new ITaskItem[] { new TaskItem(raskCoreDll), new TaskItem(exampleSharedDll) });

        Assert.True(task.Execute());

        var outDir = Path.Combine(_bundleDir, "_rask", "a");
        Assert.True(Directory.Exists(outDir), "_rask/a directory should exist after a successful bake");

        var cssBundleHash = ScopedAssetRegistry.GetBundleHash(AssetKind.Css);
        var jsBundleHash = ScopedAssetRegistry.GetBundleHash(AssetKind.Js);
        var expectedFiles = (cssBundleHash.Length > 0 ? 1 : 0) + (jsBundleHash.Length > 0 ? 1 : 0);
        Assert.True(expectedFiles > 0, "Rask.Example.Shared should register at least one scoped asset kind");

        // One concatenated bundle file per kind (css + js), not one per component.
        var bakedFiles = Directory.EnumerateFiles(outDir).ToArray();
        Assert.Equal(expectedFiles, bakedFiles.Length);
    }

    [Fact]
    public void RealAssemblies_BakedFilenamesMatchHashAndExtension()
    {
        var raskCoreDll = typeof(ScopedAssetRegistry).Assembly.Location;
        var exampleSharedDll = typeof(App).Assembly.Location;

        var task = NewTask(_bundleDir, new ITaskItem[] { new TaskItem(raskCoreDll), new TaskItem(exampleSharedDll) });
        task.Execute();

        var outDir = Path.Combine(_bundleDir, "_rask", "a");
        foreach (var (kind, ext) in new[] { (AssetKind.Css, "css"), (AssetKind.Js, "js") })
        {
            var hash = ScopedAssetRegistry.GetBundleHash(kind);
            if (hash.Length == 0)
            {
                continue;
            }

            var expected = Path.Combine(outDir, hash + "." + ext);
            Assert.True(File.Exists(expected), $"missing baked bundle at {expected}");
            var bundle = ScopedAssetRegistry.GetByHash(hash, kind);
            Assert.NotNull(bundle);
            Assert.Equal(bundle!.Value.Utf8.ToArray(), File.ReadAllBytes(expected));
        }
    }

    [Fact]
    public void FailOnEmpty_WhenAssetsBaked_ReturnsTrue()
    {
        // The real assemblies register a non-empty scoped-asset set, so even with the
        // fail-fast guard armed the bake succeeds.
        var task = NewTask(_bundleDir,
            new ITaskItem[]
            {
                new TaskItem(typeof(ScopedAssetRegistry).Assembly.Location),
                new TaskItem(typeof(App).Assembly.Location)
            });
        task.FailOnEmpty = true;

        Assert.True(task.Execute());
        Assert.NotEmpty(Directory.EnumerateFiles(Path.Combine(_bundleDir, "_rask", "a")));
        Assert.Empty(((StubBuildEngine)task.BuildEngine).Errors);
    }

    [Fact]
    public void FailOnEmpty_WhenRegistryResolvedButZeroBaked_ReturnsFalseAndLogsError()
    {
        // Rask.Core is present so the registry resolves, but we feed NO registration-
        // bearing assembly (no Rask.Example.Shared), and clear the registry first so the
        // module-initializer-populated entries from this test process don't leak in.
        // Result: registry resolved, zero entries → the guard fails the build.
        ScopedAssetRegistry.InvalidateAllCss();
        ScopedAssetRegistry.InvalidateAllJs();

        var task = NewTask(_bundleDir, new ITaskItem[] { new TaskItem(typeof(ScopedAssetRegistry).Assembly.Location) });
        task.FailOnEmpty = true;

        Assert.False(task.Execute());
        Assert.NotEmpty(((StubBuildEngine)task.BuildEngine).Errors);
    }

    [Fact]
    public void FailOnEmpty_WhenNotARaskProject_ReturnsTrue()
    {
        // No Rask.Core in the assembly set → registry never resolves → silent no-op even
        // with the guard armed (a non-Rask WASM project must not be failed by the bake).
        var task = NewTask(_bundleDir,
            new ITaskItem[] { new TaskItem(Path.Combine(_bundleDir, "does-not-exist.dll")) });
        task.FailOnEmpty = true;

        Assert.True(task.Execute());
        Assert.Empty(((StubBuildEngine)task.BuildEngine).Errors);
    }

    [Fact]
    public void Rerun_OverwritesSameFiles_Idempotent()
    {
        var raskCoreDll = typeof(ScopedAssetRegistry).Assembly.Location;
        var exampleSharedDll = typeof(App).Assembly.Location;
        var assemblies = new ITaskItem[] { new TaskItem(raskCoreDll), new TaskItem(exampleSharedDll) };

        NewTask(_bundleDir, assemblies).Execute();
        var outDir = Path.Combine(_bundleDir, "_rask", "a");
        var first = Directory.EnumerateFiles(outDir).OrderBy(p => p).ToArray();

        NewTask(_bundleDir, assemblies).Execute();
        var second = Directory.EnumerateFiles(outDir).OrderBy(p => p).ToArray();

        Assert.Equal(first, second);
    }

    // The #650 guard, pinned as the three-input decision it is. It was wrong in exactly one of these
    // rows — a project with no scoped assets, where an unrelated assembly failed to load — and nothing
    // caught it until a Microsoft.Extensions bump made an unrelated assembly fail to load. That row is
    // the second one.
    [Theory]
    // written, skipped, registryResolved, expected
    [InlineData(0, 1, false, true)]   // the real #650: registry never read, nothing baked -> fail
    [InlineData(0, 1, true, false)]   // registry READ and this project has no scoped assets -> fine
    [InlineData(0, 0, false, false)]  // nothing skipped: not this failure mode
    [InlineData(3, 1, false, false)]  // files were written despite a skip: not known to be wrong
    [InlineData(3, 0, true, false)]   // the ordinary happy path
    public void IsNodeReuseBakeFailure_OnlyWhenTheRegistryWasNeverRead(
        int written, int skipped, bool registryResolved, bool expected) =>
        Assert.Equal(expected,
            BakeScopedAssetsTask.IsNodeReuseBakeFailure(written, skipped, registryResolved));

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
            IDictionary globalProperties,
            IDictionary targetOutputs) => false;
    }
}
