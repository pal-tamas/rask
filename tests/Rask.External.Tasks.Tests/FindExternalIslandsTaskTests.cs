using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Rask.External.Tasks;

namespace Rask.External.Tasks.Tests;

/// <summary>
///     A Lit island's <c>.ts</c> and a scoped-TypeScript <c>.ts</c> go to different pipelines (#938).
/// </summary>
/// <remarks>
///     <para>
///         Both are spelled <c>Name.ts</c> beside <c>Name.cs</c>, so before this task existed each
///         feature claimed the other's files: island discovery offered every scoped file to the bundler
///         as a Lit module that never default-exported a tag name, and the scoped glob compiled every
///         island module as a component asset. The two documented opt-outs only let a project say which
///         ONE of the features it had.
///     </para>
///     <para>
///         These drive the real task against real files on disk, because the answer depends on the file
///         system as much as on the source: the sibling has to exist, in the same directory, and its
///         extension has to be the one that runtime's module carries.
///     </para>
/// </remarks>
public sealed class FindExternalIslandsTaskTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("rask-external-islands").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    /// <summary>The first direction: island discovery no longer claims a scoped file.</summary>
    [Fact]
    public void A_scoped_TypeScript_file_is_not_an_island()
    {
        Write("Panel.cs", "public sealed partial class Panel : Component { }");
        Write("Panel.ts", "export function mount(): void { }");

        var task = Run();

        Assert.Empty(task.Islands);
        Assert.Empty(task.ScopedIslands);
    }

    /// <summary>The second direction: the scoped list loses the island's module.</summary>
    [Fact]
    public void A_lit_island_is_claimed_and_removed_from_the_scoped_list()
    {
        Write("Gauge.cs", "public sealed partial class Gauge : LitComponent { }");
        Write("Gauge.ts", "export default 'rask-gauge';");

        var task = Run();

        var island = Assert.Single(task.Islands);
        Assert.Equal(Path.Combine(_root, "Gauge.ts"), island.GetMetadata("FullPath"));
        Assert.Equal("Gauge", island.GetMetadata("IslandName"));
        Assert.Equal("lit", island.GetMetadata("Runtime"));

        Assert.Equal(Path.Combine(_root, "Gauge.ts"), Assert.Single(task.ScopedIslands).ItemSpec);
    }

    /// <summary>
    ///     Both in one project, which is the configuration neither opt-out could express.
    /// </summary>
    [Fact]
    public void A_project_with_both_conventions_separates_them()
    {
        Write("Gauge.cs", "public sealed partial class Gauge : LitComponent { }");
        Write("Gauge.ts", "export default 'rask-gauge';");
        Write("Panel.cs", "public sealed partial class Panel : Component { }");
        Write("Panel.ts", "export function mount(): void { }");

        var task = Run();

        Assert.Equal("Gauge.ts", Path.GetFileName(Assert.Single(task.Islands).GetMetadata("FullPath")));
        Assert.Equal(Path.Combine(_root, "Panel.ts"), Assert.Single(RemainingScopedFiles(task)));
    }

    /// <summary>
    ///     Angular pairs with a plain <c>.ts</c> too, and its runtime rides out of the same scan.
    /// </summary>
    /// <remarks>
    ///     The runtime is not cosmetic here: it decides which adapter the generated entry wraps the
    ///     module with, and the wrong one builds, ships, loads and mounts nothing.
    /// </remarks>
    [Fact]
    public void An_angular_island_is_claimed_with_its_own_runtime()
    {
        Write("Spark.cs", "public sealed partial class Spark : AngularComponent { }");
        Write("Spark.ts", "export default class SparkComponent { }");

        Assert.Equal("angular", Assert.Single(Run().Islands).GetMetadata("Runtime"));
    }

    /// <summary>
    ///     A <c>.ts</c> beside a React island is a SCOPED file: that component's module is
    ///     <c>./Chart.tsx</c>.
    /// </summary>
    [Fact]
    public void A_ts_beside_a_jsx_island_stays_scoped()
    {
        Write("Chart.cs", "public sealed partial class Chart : ReactComponent { }");
        Write("Chart.ts", "export function mount(): void { }");

        var task = Run();

        Assert.Empty(task.Islands);
        Assert.Empty(task.ScopedIslands);
    }

    /// <summary>
    ///     The project's own intermediate base class carries the runtime down to its subclasses.
    /// </summary>
    /// <remarks>
    ///     The abstract class itself has no module and must not be claimed — nothing pairs with it, and
    ///     an island named after it would collide with the concrete one.
    /// </remarks>
    [Fact]
    public void An_island_deriving_through_its_own_base_is_claimed()
    {
        Write("Widget.cs", "public abstract partial class Widget : LitComponent { }");
        Write("Widget.ts", "export default 'rask-widget';");
        Write("Dial.cs", "public sealed partial class Dial : Widget { }");
        Write("Dial.ts", "export default 'rask-dial';");

        var island = Assert.Single(Run().Islands);

        Assert.Equal("Dial", island.GetMetadata("IslandName"));
    }

    /// <summary>
    ///     A doc comment quoting a base list declares nothing — every file in Rask has one.
    /// </summary>
    [Fact]
    public void A_base_list_inside_a_comment_declares_no_island()
    {
        Write("Panel.cs", """
                          /// <summary>Like <c>class Panel : LitComponent</c>, but not.</summary>
                          // class Panel : LitComponent
                          public sealed partial class Panel : Component { }
                          """);
        Write("Panel.ts", "export function mount(): void { }");

        Assert.Empty(Run().Islands);
    }

    /// <summary>
    ///     A primary constructor sits between the name and the base list, and used to hide it.
    /// </summary>
    [Fact]
    public void An_island_with_a_primary_constructor_is_claimed()
    {
        Write("Gauge.cs", "public sealed partial class Gauge(int scale) : LitComponent { }");
        Write("Gauge.ts", "export default 'rask-gauge';");

        Assert.Single(Run().Islands);
    }

    /// <summary>
    ///     The pairing is per DIRECTORY. A same-named file one folder over is not the module.
    /// </summary>
    [Fact]
    public void A_ts_without_a_sibling_in_its_own_folder_is_not_claimed()
    {
        Write("Gauge.cs", "public sealed partial class Gauge : LitComponent { }");
        Write(Path.Combine("Elsewhere", "Gauge.ts"), "export default 'rask-gauge';");

        Assert.Empty(Run().Islands);
    }

    /// <summary>Runs the task over everything written into the scratch project.</summary>
    /// <remarks>
    ///     Both lists are handed in as absolute paths. In a real build <c>@(_RaskScopedTs)</c> holds
    ///     PROJECT-RELATIVE identities, which MSBuild resolves through <c>%(FullPath)</c> against the
    ///     project directory — something no unit test can reproduce without setting the process-wide
    ///     working directory, which parallel test classes would then fight over. That the removal
    ///     matches those relative identities is asserted by <c>IslandScopedTsSeparationTests</c>, which
    ///     drives real MSBuild over a real project.
    /// </remarks>
    private FindExternalIslandsTask Run()
    {
        var typeScript = Directory.EnumerateFiles(_root, "*.ts", SearchOption.AllDirectories)
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();

        var task = new FindExternalIslandsTask
        {
            BuildEngine = new SilentBuildEngine(),
            Sources =
            [
                .. Directory.EnumerateFiles(_root, "*.cs", SearchOption.AllDirectories)
                    .OrderBy(f => f, StringComparer.Ordinal)
                    .Select(f => (ITaskItem)new TaskItem(f)),
            ],
            Candidates = [.. typeScript.Select(f => (ITaskItem)new TaskItem(f))],
            ScopedFiles = [.. typeScript.Select(f => (ITaskItem)new TaskItem(f))],
        };

        Assert.True(task.Execute());
        return task;
    }

    /// <summary>The scoped files the build would still compile, after the islands are removed.</summary>
    private IEnumerable<string> RemainingScopedFiles(FindExternalIslandsTask task)
    {
        var removed = task.ScopedIslands.Select(i => i.ItemSpec).ToHashSet(StringComparer.Ordinal);

        return task.ScopedFiles
            .Select(f => f.ItemSpec)
            .Where(f => !removed.Contains(f))
            .OrderBy(f => f, StringComparer.Ordinal);
    }

    private void Write(string relativePath, string contents)
    {
        var full = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, contents);
    }

    /// <summary>A build engine that keeps errors and discards the rest.</summary>
    private sealed class SilentBuildEngine : IBuildEngine
    {
        public bool ContinueOnError => false;

        public int LineNumberOfTaskNode => 0;

        public int ColumnNumberOfTaskNode => 0;

        public string ProjectFileOfTaskNode => "test.csproj";

        public bool BuildProjectFile(
            string projectFileName,
            string[] targetNames,
            System.Collections.IDictionary globalProperties,
            System.Collections.IDictionary targetOutputs) => true;

        public void LogCustomEvent(CustomBuildEventArgs e)
        {
        }

        public void LogErrorEvent(BuildErrorEventArgs e) => Assert.Fail(e.Message);

        public void LogMessageEvent(BuildMessageEventArgs e)
        {
        }

        public void LogWarningEvent(BuildWarningEventArgs e)
        {
        }
    }
}
