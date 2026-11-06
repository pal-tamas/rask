namespace Rask.Tailwind.Tasks.Tests;

// RaskTailwindPath is the way through on a platform Tailwind publishes no standalone binary for: the
// build runs the named binary as it would the fetched one, and never reaches the network for it.
public class TailwindPathTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "rask-tailwind-path-" + Guid.NewGuid().ToString("n"));

    public TailwindPathTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void A_named_binary_is_run_instead_of_the_fetched_one()
    {
        var binary = Path.Combine(_directory, "tailwindcss");
        File.WriteAllText(binary, "#!/bin/sh\n");
        var cache = Path.Combine(_directory, "cache");
        var task = new ResolveTailwindCliTask
        {
            BuildEngine = new RecordingEngine(),
            Version = TailwindPins.Version,
            CacheRoot = cache,
            ExecutablePath = binary,
            Offline = true,
        };

        var resolved = task.Execute();

        Assert.True(resolved);
        Assert.Equal(binary, task.ToolPath);
        Assert.False(Directory.Exists(cache));
    }

    [Fact]
    public void A_named_binary_that_is_not_there_fails_the_build_naming_the_property()
    {
        var engine = new RecordingEngine();
        var task = new ResolveTailwindCliTask
        {
            BuildEngine = engine,
            Version = TailwindPins.Version,
            CacheRoot = Path.Combine(_directory, "cache"),
            ExecutablePath = Path.Combine(_directory, "missing-tailwindcss"),
        };

        var resolved = task.Execute();

        Assert.False(resolved);
        Assert.Empty(task.ToolPath);
        Assert.Contains("RaskTailwindPath", Assert.Single(engine.Errors), StringComparison.Ordinal);
    }
}
