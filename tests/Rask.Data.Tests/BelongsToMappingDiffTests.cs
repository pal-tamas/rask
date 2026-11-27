using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Rask.Data.Tests.Shapes;
using Hand = Rask.Data.Tests.Shapes.HandReferenced;
using Referenced = Rask.Data.Tests.Shapes.Referenced;

namespace Rask.Data.Tests;

/// <summary>
/// An app moving a hand-written id and navigation to another aggregate — or the id and its configured
/// <c>HasOne&lt;T&gt;().WithMany()</c> — to one <c>BelongsTo&lt;T&gt;</c> writes no migration: the generated key keeps
/// the column's name, type and nullability, and the relationship its constraint, its index and its delete.
/// </summary>
public sealed class BelongsToMappingDiffTests
{
    private const string Sqlite = "Data Source=:memory:";

    // The shape ForUpdateSqlServerReadTests builds its context with: one EF Core service provider for both.
    private const string OfflineSqlServer = "Server=localhost;Database=rask;User Id=sa;Password=x;TrustServerCertificate=true";

    [Fact]
    public void On_sqlite_a_hand_written_reference_and_a_BelongsTo_create_the_same_schema()
    {
        using var hand = HandOn(o => o.UseSqlite(Sqlite));
        using var referenced = ReferencedOn(o => o.UseSqlite(Sqlite));

        var (before, after) = (hand.Database.GenerateCreateScript(), referenced.Database.GenerateCreateScript());

        Assert.Equal(before, after);
        Assert.Contains("FOREIGN KEY (\"ExcavatorId\") REFERENCES \"Excavator\" (\"Id\") ON DELETE RESTRICT", after, StringComparison.Ordinal);
    }

    [Fact]
    public void On_sqlite_the_migration_differ_finds_nothing_between_the_two_either_way()
    {
        using var hand = HandOn(o => o.UseSqlite(Sqlite));
        using var referenced = ReferencedOn(o => o.UseSqlite(Sqlite));

        var (forward, back) = ValueObjectMappingDiff.Between(hand, referenced);

        Assert.Empty(forward);
        Assert.Empty(back);
    }

    [Fact]
    public void On_sql_server_offline_a_hand_written_reference_and_a_BelongsTo_create_the_same_schema()
    {
        using var hand = HandOn(o => o.UseSqlServer(OfflineSqlServer));
        using var referenced = ReferencedOn(o => o.UseSqlServer(OfflineSqlServer));

        var (forward, back) = ValueObjectMappingDiff.Between(hand, referenced);

        Assert.Empty(forward);
        Assert.Empty(back);
        Assert.Equal(hand.Database.GenerateCreateScript(), referenced.Database.GenerateCreateScript());
    }

    [Fact]
    public void A_BelongsTo_maps_its_key_and_never_a_navigation_to_the_other_aggregate_nor_a_cascade()
    {
        using var referenced = ReferencedOn(o => o.UseSqlite(Sqlite));

        var dig = referenced.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(Referenced.Dig))!;

        Assert.Empty(dig.GetNavigations());
        Assert.Null(dig.FindProperty(nameof(Referenced.Dig.Excavator)));
        Assert.Equal(
            [("DumpedAtId", typeof(Referenced.Quarry), false, DeleteBehavior.NoAction), ("ExcavatorId", typeof(Referenced.Excavator), true, DeleteBehavior.Restrict)],
            dig.GetForeignKeys()
                .Select(static k => (k.Properties.Single().Name, k.PrincipalEntityType.ClrType, k.IsRequired, k.DeleteBehavior))
                .OrderBy(static k => k.Name, StringComparer.Ordinal));
    }

    [Fact]
    public void An_OnDelete_the_entitys_own_configuration_states_wins_over_the_BelongsTo_restrict()
    {
        using var referenced = new CascadingQuarryContext(
            OptionsLayout.LikeAddDbContext<CascadingQuarryContext>().UseSqlite(Sqlite).Options);

        var excavator = referenced.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(Referenced.Excavator))!;

        Assert.Equal(DeleteBehavior.SetNull, Assert.Single(excavator.GetForeignKeys()).DeleteBehavior);
    }

    private static Hand.HandReferencedQuarryContext HandOn(
        Func<DbContextOptionsBuilder<Hand.HandReferencedQuarryContext>, DbContextOptionsBuilder<Hand.HandReferencedQuarryContext>> provider) =>
        new(provider(OptionsLayout.LikeAddDbContext<Hand.HandReferencedQuarryContext>()).Options);

    private static Referenced.ReferencedQuarryContext ReferencedOn(
        Func<DbContextOptionsBuilder<Referenced.ReferencedQuarryContext>, DbContextOptionsBuilder<Referenced.ReferencedQuarryContext>> provider) =>
        new(provider(OptionsLayout.LikeAddDbContext<Referenced.ReferencedQuarryContext>()).Options);

    /// <summary>The quarry's model with the excavator's reference configured to null itself out, as an app may say.</summary>
    private sealed class CascadingQuarryContext(DbContextOptions<CascadingQuarryContext> options) : DbContext(options)
    {
        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
            ModelRegistry.ApplyConventions(configurationBuilder);

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Referenced.Quarry>();
            var excavator = modelBuilder.Entity<Referenced.Excavator>();
            modelBuilder.ApplyRaskConventions();
            excavator.HasOne<Referenced.Quarry>().WithMany().HasForeignKey(e => e.QuarryId).OnDelete(DeleteBehavior.SetNull);
        }
    }
}
