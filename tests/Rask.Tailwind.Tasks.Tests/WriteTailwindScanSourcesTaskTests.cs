using Microsoft.Build.Utilities;

namespace Rask.Tailwind.Tasks.Tests;

/// <summary>
///     The folder Rask.Ui's Tailwind build runs in (<c>RaskTailwindSourceRoot</c>): its C# at the same
///     paths, without the comments, and without what is no longer a source.
/// </summary>
public sealed class WriteTailwindScanSourcesTaskTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "rask-scan-" + Guid.NewGuid().ToString("N"));

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Each_source_is_written_at_its_own_path_without_its_comments()
    {
        var project = Write("project/Charts/UiChart.cs", "// h-64\nvar c = \"aspect-video\";");

        var ok = Run(project, "project/Charts/UiChart.cs");

        Assert.True(ok);
        Assert.Equal("\nvar c = \"aspect-video\";", File.ReadAllText(Path.Combine(_root, "scan", "Charts", "UiChart.cs")));
    }

    [Fact]
    public void A_copy_whose_source_is_gone_is_deleted()
    {
        var project = Write("project/UiKept.cs", "var k = \"flex\";");
        Write("scan/UiRemoved.cs", "var r = \"glass\";");

        Run(project, "project/UiKept.cs");

        Assert.True(File.Exists(Path.Combine(_root, "scan", "UiKept.cs")));
        Assert.False(File.Exists(Path.Combine(_root, "scan", "UiRemoved.cs")));
    }

    [Fact]
    public void A_copy_that_would_not_change_is_not_rewritten()
    {
        var project = Write("project/UiKept.cs", "var k = \"flex\"; // first");
        Run(project, "project/UiKept.cs");
        var copy = Path.Combine(_root, "scan", "UiKept.cs");
        var stamp = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(copy, stamp);
        File.WriteAllText(Path.Combine(_root, "project", "UiKept.cs"), "var k = \"flex\"; // second");

        Run(project, "project/UiKept.cs");

        Assert.Equal(stamp, File.GetLastWriteTimeUtc(copy));
    }

    private string Write(string relative, string text)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return Path.Combine(_root, "project");
    }

    private bool Run(string project, params string[] sources) => new WriteTailwindScanSourcesTask
    {
        BuildEngine = new RecordingEngine(),
        Sources = [.. sources.Select(s => new TaskItem(Path.Combine(_root, s)))],
        SourceRoot = project,
        Output = Path.Combine(_root, "scan"),
    }.Execute();
}
