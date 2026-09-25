using Rask.Cli.Commands;
using Rask.Cli.Scaffolding;

namespace Rask.Cli.Tests;

/// <summary>
/// What the scaffolded project gets for its database. SQLite is the only one Rask wires, and RaskApp wires it
/// — Litestream, snapshots, the restore-on-startup call — so what is left here is the settings the scaffold
/// chooses and the Dockerfile's data volume.
/// </summary>
public sealed class SqliteScaffoldTests
{
    private const string Root = "/proj/App";
    private const string Version = "9.9.9";

    private static Dictionary<string, string> Generate(params string[] flags) =>
        ProjectGenerator.GenerateServer(Root, "App", NewCommand.BatteriesOf(flags), Version).Files
            .ToDictionary(
                f => Path.GetRelativePath(Root, f.Path).Replace('\\', '/'),
                f => f.Content,
                StringComparer.Ordinal);

    [Fact]
    public void Data_chooses_its_database_file_and_strict_tables_in_appsettings()
    {
        var files = Generate("data");

        // STRICT is on for a new app: it costs nothing at creation time and is awkward to adopt once
        // there is data, so the scaffold is the one moment to choose it. Both it and the database file are
        // settings, so they are chosen in appsettings.json; RaskApp wires SQLite itself.
        Assert.Contains("\"StrictTables\": true", files["appsettings.json"], StringComparison.Ordinal);
        Assert.Contains("Data Source=app.db", files["appsettings.json"], StringComparison.Ordinal);
        Assert.DoesNotContain("c.Data.Off()", files["Program.cs"], StringComparison.Ordinal);
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
        var next = ProjectGenerator.GenerateServer(Root, "App", NewCommand.BatteriesOf(["data"]), Version).Notes;

        Assert.NotNull(next);
        Assert.Contains("rask db update", next, StringComparison.Ordinal);
        Assert.Contains("app.db", next, StringComparison.Ordinal);
        // Not the FIRST migration any more — `rask new` creates and applies that itself.
        Assert.DoesNotContain("rask db add Init", next, StringComparison.Ordinal);
    }
}
