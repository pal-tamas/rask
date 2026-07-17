using Rask.Cli.Scaffolding;

namespace Rask.Cli.Tests;

public sealed class GenerateConfigTests
{
    [Fact]
    public void Round_trips_through_the_filesystem()
    {
        var fs = new FakeFileSystem();
        new GenerateConfig { Bs = true, Tests = true, Validation = "fluent", Id = "int" }.Save(fs, "/proj");

        var loaded = GenerateConfig.Load(fs, "/proj");

        Assert.True(loaded.Bs);
        Assert.True(loaded.Tests);
        Assert.Equal("fluent", loaded.Validation);
        Assert.Equal("int", loaded.Id);
        Assert.Null(loaded.Modal); // absent stays null (off), not false
    }

    [Fact]
    public void Absent_file_loads_an_empty_config()
    {
        var loaded = GenerateConfig.Load(new FakeFileSystem(), "/proj");

        Assert.Null(loaded.Bs);
        Assert.Null(loaded.Validation);
    }

    [Fact]
    public void Corrupt_file_falls_back_to_empty_rather_than_throwing()
    {
        var fs = new FakeFileSystem();
        fs.Seed(GenerateConfig.PathFor("/proj"), "{ not valid json");

        var loaded = GenerateConfig.Load(fs, "/proj");

        Assert.Null(loaded.Bs);
    }
}
