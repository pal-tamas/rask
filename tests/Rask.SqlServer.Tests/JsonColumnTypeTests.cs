using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Rask.Data;

namespace Rask.SqlServer.Tests;

public sealed record Attachment(string Name, int Size);

public sealed record Stop(string Place);

/// <summary>A filing whose files, stops and pinned stops are each kept as JSON in one column.</summary>
public sealed class Filing
{
    public Guid Id { get; set; }

    public List<Attachment> Files { get; set; } = [];

    public List<Stop> Stops { get; set; } = [];

    public List<Stop> Pinned { get; set; } = [];

    public List<string> Tags { get; set; } = [];
}

/// <summary>JSON containers left to Rask's convention, one of them typed by the app itself.</summary>
public sealed class JsonColumnsContext(DbContextOptions<JsonColumnsContext> options) : RaskDbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<Filing>(b =>
        {
            b.ComplexCollection(f => f.Files, files => files.ToJson());
            b.OwnsMany(f => f.Stops, stops => stops.ToJson());
            b.ComplexCollection(f => f.Pinned, pinned => pinned.ToJson().Metadata.ComplexType.SetContainerColumnType("nvarchar(4000)"));
        });
    }
}

/// <summary>The same model as an app pinned it by hand before Rask did: every container typed <c>nvarchar(max)</c>.</summary>
public sealed class HandPinnedJsonColumnsContext(DbContextOptions<HandPinnedJsonColumnsContext> options) : RaskDbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<Filing>(b =>
        {
            b.ComplexCollection(f => f.Files, files => files.ToJson().Metadata.ComplexType.SetContainerColumnType("nvarchar(max)"));
            b.OwnsMany(f => f.Stops, stops => stops.ToJson().OwnedEntityType.SetContainerColumnType("nvarchar(max)"));
            b.ComplexCollection(f => f.Pinned, pinned => pinned.ToJson().Metadata.ComplexType.SetContainerColumnType("nvarchar(4000)"));
        });
    }
}

/// <summary>A JSON container on SQL Server is <c>nvarchar(max)</c> at every compatibility level.</summary>
public sealed class JsonColumnTypeTests
{
    [Theory]
    [InlineData(150)]
    [InlineData(160)]
    [InlineData(170)]
    public void A_json_container_is_nvarchar_max_at_every_compatibility_level(int level)
    {
        using var context = new JsonColumnsContext(Options<JsonColumnsContext>(level));

        var script = context.Database.GenerateCreateScript();

        Assert.Contains("[Files] nvarchar(max) NOT NULL", script, StringComparison.Ordinal);
        Assert.Contains("[Stops] nvarchar(max)", script, StringComparison.Ordinal);
        Assert.Contains("[Tags] nvarchar(max) NOT NULL", script, StringComparison.Ordinal);
        Assert.DoesNotContain(" json", script, StringComparison.Ordinal);
    }

    [Fact]
    public void A_container_type_the_app_states_wins()
    {
        using var context = new JsonColumnsContext(Options<JsonColumnsContext>(170));

        var script = context.Database.GenerateCreateScript();

        Assert.Contains("[Pinned] nvarchar(4000) NOT NULL", script, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(160)]
    [InlineData(170)]
    public void Dropping_the_hand_pinned_type_is_no_migration(int level)
    {
        using var pinned = new HandPinnedJsonColumnsContext(Options<HandPinnedJsonColumnsContext>(level));
        using var left = new JsonColumnsContext(Options<JsonColumnsContext>(level));

        var operations = left.GetService<IMigrationsModelDiffer>().GetDifferences(
            pinned.GetService<IDesignTimeModel>().Model.GetRelationalModel(),
            left.GetService<IDesignTimeModel>().Model.GetRelationalModel());

        Assert.Empty(operations);
    }

    private static DbContextOptions<TContext> Options<TContext>(int level)
        where TContext : DbContext =>
        new DbContextOptionsBuilder<TContext>()
            .UseSqlServer("Server=unused;Database=unused", sql => sql.UseCompatibilityLevel(level)).Options;
}
