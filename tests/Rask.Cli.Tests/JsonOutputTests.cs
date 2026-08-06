using System.Text.Json;
using Rask.Cli.Commands;

namespace Rask.Cli.Tests;

/// <summary>
///     The <c>--json</c> surface (#600): the commands worth scripting emit a document and nothing else.
/// </summary>
public class JsonOutputTests
{
    /// <summary>
    ///     Real <c>dotnet ef migrations list --json</c> output, captured from a live EF project rather
    ///     than written from memory — including the build preamble and the tools-version warning, which
    ///     are the whole reason the payload has to be found rather than assumed to start at byte zero.
    /// </summary>
    private const string RealEfOutput =
        """
        Build started...
        Build succeeded.
        The Entity Framework tools version '10.0.5' is older than that of the runtime '10.0.10'. Update the tools for the latest features and bug fixes. See https://aka.ms/AAc1fbw for more information.
        [
          {
            "id": "20260806131732_First",
            "name": "First",
            "safeName": "First",
            "applied": false
          },
          {
            "id": "20260806131734_Second",
            "name": "Second",
            "safeName": "Second",
            "applied": true
          }
        ]
        """;

    [Fact]
    public void The_migration_payload_is_found_past_ef_s_preamble()
    {
        var payload = DbCommand.ExtractJsonArray(RealEfOutput);

        Assert.NotNull(payload);
        var migrations = JsonSerializer.Deserialize(payload!, CliJsonContext.Default.EfMigrationArray);

        Assert.NotNull(migrations);
        Assert.Equal(2, migrations!.Length);
        Assert.Equal("20260806131732_First", migrations[0].Id);
        Assert.False(migrations[0].Applied);
        Assert.True(migrations[1].Applied);
    }

    [Fact]
    public void Output_with_no_document_is_reported_rather_than_guessed_at()
    {
        // EF failing before it prints anything must not become an empty migration list, which would
        // read as "this project has no migrations".
        Assert.Null(DbCommand.ExtractJsonArray("Build started...\nBuild FAILED.\n"));
        Assert.Null(DbCommand.ExtractJsonArray(string.Empty));
    }

    [Fact]
    public void Info_json_is_a_document_and_nothing_else()
    {
        var report = new InfoReport("1.2.3", "10.0.100", "macOS 26.5.1");

        var json = JsonSerializer.Serialize(report, CliJsonContext.Default.InfoReport);

        // Parses as one object, with the camelCase names a script would key off.
        using var parsed = JsonDocument.Parse(json);
        Assert.Equal("1.2.3", parsed.RootElement.GetProperty("raskCli").GetString());
        Assert.Equal("10.0.100", parsed.RootElement.GetProperty("dotnetSdk").GetString());
        Assert.Equal("macOS 26.5.1", parsed.RootElement.GetProperty("os").GetString());
    }

    [Fact]
    public void A_missing_sdk_is_absent_from_the_json_rather_than_the_string_not_found()
    {
        // The human report prints "not found", which is prose. A script wants the field to be missing.
        var json = JsonSerializer.Serialize(
            new InfoReport("1.2.3", null, "linux"), CliJsonContext.Default.InfoReport);

        using var parsed = JsonDocument.Parse(json);
        Assert.False(parsed.RootElement.TryGetProperty("dotnetSdk", out _));
        Assert.DoesNotContain("not found", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Deploy_status_json_keeps_the_fields_the_table_folds_together()
    {
        // The human table collapses domain/ports into one "URL" column and substitutes
        // "(not published)" — right for reading, wrong for parsing.
        var report = new DeployStatusReport("box", [
            new DeployedAppStatus("shop", "shop-blue", "shop.example.com", null, "blue", "Up 2 hours", true),
            new DeployedAppStatus("blog", "blog-green", null, null, null, "Exited (1)", false),
        ]);

        var json = JsonSerializer.Serialize(report, CliJsonContext.Default.DeployStatusReport);

        using var parsed = JsonDocument.Parse(json);
        var apps = parsed.RootElement.GetProperty("apps");
        Assert.Equal("shop.example.com", apps[0].GetProperty("domain").GetString());
        Assert.True(apps[0].GetProperty("isCurrentProject").GetBoolean());

        // An app with no domain and no ports has neither field, rather than "(not published)".
        Assert.False(apps[1].TryGetProperty("domain", out _));
        Assert.False(apps[1].TryGetProperty("ports", out _));
        Assert.DoesNotContain("not published", json, StringComparison.Ordinal);
    }
}
