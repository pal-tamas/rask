namespace Rask.External.Tasks.Tests;

public sealed class GeneratedTypeScriptTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "rask-external-tasks-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void An_assembly_without_the_type_carries_nothing()
    {
        var assembly = typeof(GeneratedTypeScriptTests).Assembly.Location;

        var constants = GeneratedTypeScript.Read(assembly, "Rask.External.Generated", "RaskExternalGeneratedTypeScript");

        Assert.Empty(constants);
    }

    [Fact]
    public void An_unchanged_file_is_not_rewritten()
    {
        // Load-bearing: these files sit in the bundler's watched tree, so touching them every build
        // makes a watch build re-trigger itself.
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "Chart.props.d.ts");
        GeneratedTypeScript.WriteIfDifferent(path, "export type A = string;");
        var stamp = File.GetLastWriteTimeUtc(path);

        var rewroteSame = GeneratedTypeScript.WriteIfDifferent(path, "export type A = string;");
        var stampAfterSame = File.GetLastWriteTimeUtc(path);
        var rewroteChanged = GeneratedTypeScript.WriteIfDifferent(path, "export type A = number;");

        Assert.False(rewroteSame);
        Assert.Equal(stamp, stampAfterSame);
        Assert.True(rewroteChanged);
        Assert.Equal("export type A = number;", File.ReadAllText(path));
    }

    [Fact]
    public void Writing_creates_the_directory()
    {
        var path = Path.Combine(_directory, "obj", "rask-external", "Chart.props.d.ts");

        var wrote = GeneratedTypeScript.WriteIfDifferent(path, "export {};");

        Assert.True(wrote);
        Assert.True(File.Exists(path));
    }
}
