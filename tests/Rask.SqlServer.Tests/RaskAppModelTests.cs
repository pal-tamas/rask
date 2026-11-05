using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Rask.Background;
using Rask.Caching;
using Rask.Data;

namespace Rask.SqlServer.Tests;

/// <summary>Every battery on, with every string capped at one character unless its battery says otherwise.</summary>
public sealed class CappedStringsContext(DbContextOptions<RaskAppDbContext> options) : RaskAppDbContext(options)
{
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);
        configurationBuilder.Properties<string>().HaveMaxLength(1);
    }
}

/// <summary>Rask's context under TA's rules, with one battery table renamed past SQL Server's identifier limit.</summary>
public sealed class RuledAppContext(DbContextOptions<RaskAppDbContext> options) : RaskAppDbContext(options)
{
    internal static readonly string LongTable = new('c', 125);

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);
        configurationBuilder.Properties<string>().HaveMaxLength(1000);
        configurationBuilder.ConstraintNames(n => n.Kind switch
        {
            ConstraintKind.PrimaryKey => $"{n.Table}_pkey",
            ConstraintKind.ForeignKey => $"{n.Table}_{n.Columns[0]}_fkey",
            ConstraintKind.Unique => $"{n.Table}_{n.Joined}_key",
            ConstraintKind.Index => $"{n.Table}_{n.Joined}_idx",
            ConstraintKind.Default => $"{n.Table}_{n.Columns[0]}_df",
            _ => null,
        });
        configurationBuilder.DeleteBehavior(children: DeleteBehavior.ClientCascade, others: DeleteBehavior.NoAction);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<CacheEntry>().ToTable(LongTable);
        modelBuilder.Entity<Job>().Property(j => j.Attempts).HasDefaultValue(0);
        modelBuilder.Entity<Job>().Property(j => j.Lane).HasDefaultValue("main")
            .Metadata.SetDefaultConstraintName("jobs_lane_as_written");
    }
}

/// <summary>Rask's context naming Hungarian as the order text is put in, when a query asks for it.</summary>
public sealed class HungarianOrderedContext(DbContextOptions<RaskAppDbContext> options) : RaskAppDbContext(options)
{
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);
        configurationBuilder.OrderTextBy("Hungarian_100_CI_AS");
    }
}

/// <summary>
/// Rask's own context on SQL Server, model only: the batteries' columns under an app-wide string length, and the app's
/// constraint names under SQL Server's 128-character identifier limit.
/// </summary>
public sealed class RaskAppModelTests
{
    [Fact]
    public void Every_battery_column_states_its_own_length_so_an_app_wide_one_never_truncates_it()
    {
        using var context = new CappedStringsContext(Options());

        var model = context.GetService<IDesignTimeModel>().Model;

        var capped = model.GetEntityTypes()
            .SelectMany(e => e.GetProperties().Where(p => p.ClrType == typeof(string)).Select(p => (Entity: e, Property: p)))
            .Where(x => x.Property.GetMaxLength() == 1)
            .Select(x => $"{x.Entity.ClrType.Name}.{x.Property.Name}")
            .ToList();
        Assert.True(capped.Count == 0, "Capped by the app-wide length: " + string.Join(", ", capped));
        Assert.Equal("nvarchar(max)", model.FindEntityType(typeof(Job))!.FindProperty(nameof(Job.Payload))!.GetColumnType());
    }

    [Fact]
    public void An_app_s_constraint_names_reach_every_battery_table_and_are_cut_to_SQL_Server_s_128()
    {
        using var context = new RuledAppContext(Options());

        var model = context.GetService<IDesignTimeModel>().Model;

        var jobs = model.FindEntityType(typeof(Job))!;
        var table = jobs.GetTableName();
        Assert.Equal($"{table}_pkey", jobs.FindPrimaryKey()!.GetName());
        Assert.Equal($"{table}_WaitingKey_key", jobs.GetIndexes().Single(i => i.IsUnique).GetDatabaseName());
        Assert.Equal($"{table}_Lane_ProcessedAt_idx", jobs.GetIndexes().Single(i => i.Properties[0].Name == nameof(Job.Lane)).GetDatabaseName());
        Assert.Equal($"{table}_Attempts_df", jobs.FindProperty(nameof(Job.Attempts))!.GetDefaultConstraintName());
        Assert.Equal("jobs_lane_as_written", jobs.FindProperty(nameof(Job.Lane))!.GetDefaultConstraintName());
        var cut = model.FindEntityType(typeof(CacheEntry))!.FindPrimaryKey()!.GetName();
        Assert.Equal($"{RuledAppContext.LongTable}_pkey"[..128], cut);
    }

    [Fact]
    public void An_app_s_text_order_is_written_into_the_order_by_only_and_never_into_a_column()
    {
        using var context = new HungarianOrderedContext(Options());

        var sql = context.Set<Job>().Where(j => j.Lane == "main").OrderBy(j => j.Type.InTextOrder()).ToQueryString();
        var model = context.GetService<IDesignTimeModel>().Model;

        Assert.Contains("ORDER BY [j].[Type] COLLATE Hungarian_100_CI_AS", sql, StringComparison.Ordinal);
        Assert.Contains("WHERE [j].[Lane] = N'main'", sql, StringComparison.Ordinal);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(sql, "COLLATE"));
        var texts = model.GetEntityTypes().SelectMany(e => e.GetProperties()).Where(p => p.ClrType == typeof(string));
        Assert.All(texts, p => Assert.Null(p.GetCollation()));
    }

    private static DbContextOptions<RaskAppDbContext> Options() =>
        new DbContextOptionsBuilder<RaskAppDbContext>().UseSqlServer("Server=unused;Database=unused").Options;
}
