using Rask.Cli.Commands;
using Rask.Cli.Scaffolding;

namespace Rask.Cli.Tests;

/// <summary>
/// What the database provider changes about a deploy. The volume and the injected connection string exist
/// because the database is a file on the box; a client-server database has neither, and must not be handed
/// a guessed connection string in their place.
/// </summary>
public sealed class DeployDatabaseProviderTests
{
    private const string WorkingDir = "/proj";
    private const string Host = "deploy@box";

    private static string Csproj(string package) =>
        $"<Project Sdk=\"Microsoft.NET.Sdk.Web\"><ItemGroup><PackageReference Include=\"{package}\" Version=\"1.0.0\"/></ItemGroup></Project>";

    [Fact]
    public void Sqlite_still_gets_its_volume_and_connection_string()
    {
        var args = DeployCommand.BuildRunArguments(Host, "shop", domain: null, color: null, 8080, []);

        Assert.Contains("shop-data:/data", args, StringComparer.Ordinal);
        Assert.Contains("ConnectionStrings__App=Data Source=/data/app.db", args, StringComparer.Ordinal);
    }

    [Fact]
    public void Postgres_gets_neither_a_volume_nor_an_invented_connection_string()
    {
        var args = DeployCommand.BuildRunArguments(
            Host, "shop", domain: null, color: null, 8080, [], provider: DatabaseProvider.Postgres);

        Assert.DoesNotContain(args, a => a.Contains("shop-data", StringComparison.Ordinal));
        Assert.DoesNotContain(args, a => a.StartsWith("ConnectionStrings__App=", StringComparison.Ordinal));

        // The rest of the container hygiene is untouched.
        Assert.Contains("ASPNETCORE_ENVIRONMENT=Production", args, StringComparer.Ordinal);
        Assert.Contains("no-new-privileges", args, StringComparer.Ordinal);
    }

    [Fact]
    public void Postgres_passes_the_users_own_connection_string_through()
    {
        var args = DeployCommand.BuildRunArguments(
            Host, "shop", domain: null, color: null, 8080,
            ["ConnectionStrings__App=Host=db.internal;Database=shop"],
            provider: DatabaseProvider.Postgres);

        Assert.Contains("ConnectionStrings__App=Host=db.internal;Database=shop", args, StringComparer.Ordinal);
        Assert.Single(args, a => a.StartsWith("ConnectionStrings__App=", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Deploying_a_postgres_app_without_a_connection_string_is_refused()
    {
        // It would otherwise start against the placeholder localhost string baked into Program.cs — on the
        // server that is either nothing, or somebody else's database. Failing is the only safe answer.
        var console = new StringConsole();
        var fs = new FakeFileSystem();
        fs.Seed("/proj/App.csproj", Csproj("Rask.Postgres"));
        fs.Seed("/proj/Dockerfile", "FROM scratch");
        var command = new DeployCommand(console, fs, new FakeProcessRunner(), WorkingDir);

        var exit = await command.ExecuteAsync(["--host", Host, "--dry-run"], CancellationToken.None);

        Assert.Equal(1, exit);
        Assert.Contains("needs a connection string", console.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Deploying_a_postgres_app_with_a_connection_string_proceeds()
    {
        var console = new StringConsole();
        var fs = new FakeFileSystem();
        fs.Seed("/proj/App.csproj", Csproj("Rask.Postgres"));
        fs.Seed("/proj/Dockerfile", "FROM scratch");
        var command = new DeployCommand(console, fs, new FakeProcessRunner(), WorkingDir);

        var exit = await command.ExecuteAsync(
            ["--host", Host, "--env", "ConnectionStrings__App=Host=db;Database=app", "--dry-run"],
            CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.DoesNotContain("needs a connection string", console.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_postgres_deploy_does_not_nag_about_litestream()
    {
        // Litestream replicates a SQLite write-ahead log. Warning about it on PostgreSQL would send someone
        // configuring a replica that can never do anything.
        var console = new StringConsole();
        var fs = new FakeFileSystem();
        fs.Seed("/proj/App.csproj", Csproj("Rask.Postgres"));
        fs.Seed("/proj/Dockerfile", "FROM scratch");
        var command = new DeployCommand(console, fs, new FakeProcessRunner(), WorkingDir);

        await command.ExecuteAsync(
            ["--host", Host, "--env", "ConnectionStrings__App=Host=db;Database=app", "--dry-run"],
            CancellationToken.None);

        Assert.DoesNotContain("Litestream", console.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_sqlite_deploy_still_nags_about_litestream()
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
