using Rask.Cli.Commands;
using Rask.Cli.Scaffolding;

namespace Rask.Cli.Tests;

/// <summary>
/// What the scaffolded project gets for its database. SQLite is the only one Rask wires, so the file-based
/// machinery — Litestream, snapshots, the restore-on-startup call, the Dockerfile's data volume — is
/// unconditional, and these pin that it stays that way. The full generated output is pinned elsewhere
/// (<see cref="ShopProvenanceTests"/>); this is about the database wiring specifically.
/// </summary>
public sealed class SqliteScaffoldTests
{
    private const string Root = "/proj/App";
    private const string Version = "9.9.9";

    private static Dictionary<string, string> Generate(params string[] flags) =>
        ProjectGenerator.GenerateServer(Root, "App", NewCommand.ToBatteries(flags), Version).Files
            .ToDictionary(
                f => Path.GetRelativePath(Root, f.Path).Replace('\\', '/'),
                f => f.Content,
                StringComparer.Ordinal);

    [Fact]
    public void Data_wires_UseRaskSqlite_and_its_package()
    {
        var files = Generate("data");

        Assert.Contains("using Rask.SQLite;", files["Program.cs"], StringComparison.Ordinal);
        Assert.Contains(".UseRaskSqlite(connectionString)", files["Program.cs"], StringComparison.Ordinal);
        Assert.Contains("Data Source=app.db", files["Program.cs"], StringComparison.Ordinal);
        Assert.Contains(
            "<PackageReference Include=\"Rask.SQLite.EntityFrameworkCore\"",
            files["App.csproj"],
            StringComparison.Ordinal);
    }

    [Fact]
    public void Sqlite_keeps_all_of_it()
    {
        var files = Generate("all-batteries");
        var program = files["Program.cs"];

        Assert.Contains("AddRaskSqliteLitestream", program, StringComparison.Ordinal);
        Assert.Contains("AddRaskSqliteSnapshots", program, StringComparison.Ordinal);
        Assert.Contains("RestoreSqliteFromLitestreamAsync", program, StringComparison.Ordinal);
        Assert.Contains("<PackageReference Include=\"Rask.SQLite.Litestream\"", files["App.csproj"], StringComparison.Ordinal);
    }

    [Fact]
    public void The_dockerfile_keeps_the_data_volume_for_an_app_without_data_yet()
    {
        // `rask deploy` mounts the volume for every app, so adding --data later must not require
        // regenerating the Dockerfile.
        var dockerfile = Generate("docker")["Dockerfile"];

        Assert.Contains("mkdir -p /data", dockerfile, StringComparison.Ordinal);
        Assert.DoesNotContain("litestream", dockerfile, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_next_steps_name_the_database_file_the_migration_lands_in()
    {
        var next = ProjectGenerator.GenerateServer(Root, "App", NewCommand.ToBatteries(["data"]), Version).Notes;

        Assert.NotNull(next);
        Assert.Contains("rask db update", next, StringComparison.Ordinal);
        Assert.Contains("app.db", next, StringComparison.Ordinal);
    }
}
