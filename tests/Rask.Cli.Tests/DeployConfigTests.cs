using Rask.Cli.Scaffolding;

namespace Rask.Cli.Tests;

public sealed class DeployConfigTests
{
    private const string WorkingDir = "/proj";

    [Fact]
    public void Load_returns_empty_when_no_file()
    {
        var config = DeployConfig.Load(new FakeFileSystem(), WorkingDir);

        Assert.Null(config.Host);
        Assert.Null(config.Domain);
    }

    [Fact]
    public void Save_then_load_round_trips_the_settings()
    {
        var fs = new FakeFileSystem();
        new DeployConfig { Host = "deploy@box", Domain = "app.example.com", Name = "shop", EnvFile = ".env.prod" }
            .Save(fs, WorkingDir);

        var loaded = DeployConfig.Load(fs, WorkingDir);

        Assert.Equal("deploy@box", loaded.Host);
        Assert.Equal("app.example.com", loaded.Domain);
        Assert.Equal("shop", loaded.Name);
        Assert.Equal(".env.prod", loaded.EnvFile);
    }

    [Fact]
    public void Save_writes_to_dot_rask_deploy_json()
    {
        var fs = new FakeFileSystem();

        new DeployConfig { Host = "deploy@box" }.Save(fs, WorkingDir);

        Assert.Contains(fs.Files.Keys, k => k.EndsWith(Path.Combine(".rask", "deploy.json"), StringComparison.Ordinal));
    }

    [Fact]
    public void Load_tolerates_a_corrupt_file()
    {
        var fs = new FakeFileSystem();
        fs.Seed(DeployConfig.PathFor(WorkingDir), "{ not valid json");

        var config = DeployConfig.Load(fs, WorkingDir);

        Assert.Null(config.Host);
    }
}
