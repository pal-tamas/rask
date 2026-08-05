using Rask.Cli.Commands;
using Rask.Cli.Scaffolding;

namespace Rask.Cli.Tests;

/// <summary>
/// What choosing a database changes in the scaffolded project. SQLite is the default and its output is
/// pinned elsewhere (<see cref="ShopProvenanceTests"/>); these assert that PostgreSQL gets the right wiring
/// and, just as importantly, that it does <em>not</em> get the file-only machinery.
/// </summary>
public sealed class DatabaseProviderScaffoldTests
{
    private const string Root = "/proj/App";
    private const string Version = "9.9.9";

    private static Dictionary<string, string> Generate(DatabaseProvider provider, params string[] flags) =>
        ProjectGenerator.GenerateServer(Root, "App", NewCommand.ToBatteries(flags, provider), Version).Files
            .ToDictionary(
                f => Path.GetRelativePath(Root, f.Path).Replace('\\', '/'),
                f => f.Content,
                StringComparer.Ordinal);

    [Fact]
    public void Postgres_wires_UseRaskPostgres_and_its_package()
    {
        var files = Generate(DatabaseProvider.Postgres, "data");

        Assert.Contains("using Rask.Postgres;", files["Program.cs"], StringComparison.Ordinal);
        Assert.Contains(".UseRaskPostgres(connectionString)", files["Program.cs"], StringComparison.Ordinal);
        Assert.Contains("<PackageReference Include=\"Rask.Postgres\"", files["App.csproj"], StringComparison.Ordinal);
    }

    [Fact]
    public void Postgres_defaults_the_connection_string_to_a_local_server()
    {
        var files = Generate(DatabaseProvider.Postgres, "data");

        Assert.Contains(
            "GetConnectionString(\"App\") ?? \"Host=localhost;Database=app;Username=postgres;Password=postgres\"",
            files["Program.cs"],
            StringComparison.Ordinal);
        Assert.DoesNotContain("Data Source=app.db", files["Program.cs"], StringComparison.Ordinal);
    }

    [Fact]
    public void Postgres_gets_no_litestream_no_snapshots_and_no_sqlite_packages()
    {
        // Every one of these replicates or copies a *file*. Shipping them against a client-server database
        // would scaffold a backup story that silently cannot work — the worst kind to discover late.
        var files = Generate(DatabaseProvider.Postgres, "all-batteries");
        var program = files["Program.cs"];
        var csproj = files["App.csproj"];

        Assert.DoesNotContain("Litestream", program, StringComparison.Ordinal);
        Assert.DoesNotContain("AddRaskSqliteSnapshots", program, StringComparison.Ordinal);
        Assert.DoesNotContain("SqliteConnectionStringBuilder", program, StringComparison.Ordinal);
        Assert.DoesNotContain("RestoreSqliteFromLitestreamAsync", program, StringComparison.Ordinal);
        Assert.DoesNotContain("using Microsoft.Data.Sqlite;", program, StringComparison.Ordinal);
        Assert.DoesNotContain("Rask.SQLite", csproj, StringComparison.Ordinal);
        Assert.DoesNotContain("RaskLitestreamDownload", csproj, StringComparison.Ordinal);
    }

    [Fact]
    public void Sqlite_keeps_all_of_it()
    {
        // The mirror of the test above: the file-based path must not lose anything to the provider gating.
        var files = Generate(DatabaseProvider.Sqlite, "all-batteries");
        var program = files["Program.cs"];

        Assert.Contains("AddRaskSqliteLitestream", program, StringComparison.Ordinal);
        Assert.Contains("AddRaskSqliteSnapshots", program, StringComparison.Ordinal);
        Assert.Contains("RestoreSqliteFromLitestreamAsync", program, StringComparison.Ordinal);
        Assert.Contains("<PackageReference Include=\"Rask.SQLite.Litestream\"", files["App.csproj"], StringComparison.Ordinal);
    }

    [Fact]
    public void All_batteries_on_postgres_still_wires_every_database_backed_pillar()
    {
        // Dropping snapshots must not quietly drop the pillars that do work on any provider.
        var program = Generate(DatabaseProvider.Postgres, "all-batteries")["Program.cs"];

        Assert.Contains("AddRaskJobs<AppDbContext>()", program, StringComparison.Ordinal);
        Assert.Contains("AddRaskMail<AppDbContext>(", program, StringComparison.Ordinal);
        Assert.Contains("AddRaskCache<AppDbContext>()", program, StringComparison.Ordinal);
        Assert.Contains("AddRaskOutbox<AppDbContext>()", program, StringComparison.Ordinal);
    }

    [Fact]
    public void All_batteries_drops_snapshots_on_postgres_rather_than_failing()
    {
        // --all-batteries means "every battery that applies here", so the expansion narrows. An explicitly
        // requested --snapshots is a different case and is rejected by NewCommand instead.
        var batteries = NewCommand.ToBatteries(["all-batteries"], DatabaseProvider.Postgres);

        Assert.False(batteries.Snapshots);
        Assert.False(batteries.AnySqliteOps);
        Assert.True(batteries.Jobs);
    }

    [Fact]
    public void The_dockerfile_carries_neither_litestream_nor_a_data_volume_on_postgres()
    {
        var dockerfile = Generate(DatabaseProvider.Postgres, "data", "docker")["Dockerfile"];

        Assert.DoesNotContain("litestream", dockerfile, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/data", dockerfile, StringComparison.Ordinal);
    }

    [Fact]
    public void The_dockerfile_keeps_the_data_volume_for_a_sqlite_app_without_data_yet()
    {
        // `rask deploy` mounts the volume for any file-database app, so adding --data later must not
        // require regenerating the Dockerfile.
        var dockerfile = Generate(DatabaseProvider.Sqlite, "docker")["Dockerfile"];

        Assert.Contains("mkdir -p /data", dockerfile, StringComparison.Ordinal);
        Assert.DoesNotContain("litestream", dockerfile, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_next_steps_do_not_promise_an_app_db_file_on_postgres()
    {
        var next = ProjectGenerator.GenerateServer(
            Root, "App", NewCommand.ToBatteries(["data"], DatabaseProvider.Postgres), Version).Notes;

        Assert.NotNull(next);
        Assert.Contains("rask db update", next, StringComparison.Ordinal);
        Assert.DoesNotContain("app.db", next, StringComparison.Ordinal);
    }
}
