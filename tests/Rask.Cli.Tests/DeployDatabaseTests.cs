using Rask.Cli.Commands;

namespace Rask.Cli.Tests;

/// <summary>
/// What a deploy does about the database. Every Rask app keeps it as a file on the box, so the named volume
/// and the injected connection string are unconditional — without them the database would live in the
/// container's writable layer and be destroyed by the very redeploy it has to survive.
/// </summary>
public sealed class DeployDatabaseTests
{
    private const string WorkingDir = "/proj";
    private const string Host = "deploy@box";

    private static string Csproj(string package) =>
        $"<Project Sdk=\"Microsoft.NET.Sdk.Web\"><ItemGroup><PackageReference Include=\"{package}\" Version=\"1.0.0\"/></ItemGroup></Project>";

    [Fact]
    public void The_run_arguments_carry_the_volume_and_the_connection_string()
    {
        var args = DeployCommand.BuildRunArguments(Host, "shop", domain: null, color: null, 8080, []);

        Assert.Contains("shop-data:/data", args, StringComparer.Ordinal);
        Assert.Contains("ConnectionStrings__App=Data Source=/data/app.db", args, StringComparer.Ordinal);
    }

    [Fact]
    public async Task A_deploy_nags_about_litestream()
    {
        var console = new StringConsole();
        var fs = new FakeFileSystem();
        fs.Seed("/proj/App.csproj", Csproj("Rask.SQLite.EntityFrameworkCore"));
        fs.Seed("/proj/Dockerfile", "FROM scratch");
        var runner = new FakeProcessRunner { CaptureHandler = Deployable };
        var command = new DeployCommand(console, fs, runner, WorkingDir)
        {
            ReadinessDelay = TimeSpan.Zero,
            ReadinessAttempts = 1,
        };

        await command.ExecuteAsync(["--host", Host], CancellationToken.None);

        Assert.Contains("No Litestream replica configured", console.ErrorText, StringComparison.Ordinal);
    }

    /// <summary>A host that is already set up and a container that comes up — the boring deploy path.</summary>
    private static ProcessResult Deployable(IReadOnlyList<string> args) =>
        DeployCommandTests.IsHostProbe(args) ? new ProcessResult(0, DeployCommandTests.ReadyHostProbe, string.Empty)
        : args.Contains("inspect") ? new ProcessResult(0, "true\n", string.Empty)
        : new ProcessResult(0, string.Empty, string.Empty);
}
