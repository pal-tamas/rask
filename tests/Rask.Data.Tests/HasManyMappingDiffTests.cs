using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Rask.Data.Tests.Shapes;
using Hand = Rask.Data.Tests.Shapes.HandListed;
using Listed = Rask.Data.Tests.Shapes.Listed;

namespace Rask.Data.Tests;

/// <summary>
/// An app moving its children from a private <c>List&lt;X&gt;</c> and a read-only view to one <c>HasMany&lt;X&gt;</c>
/// writes no migration: the navigation keeps its name, the child its foreign key, and no column is added unless the
/// order is asked for with <c>InOrder</c>.
/// </summary>
public sealed class HasManyMappingDiffTests
{
    private const string Sqlite = "Data Source=:memory:";

    [Fact]
    public void On_sqlite_a_hand_written_child_list_and_a_HasMany_create_the_same_schema()
    {
        using var hand = new Hand.HandListedRackContext(OptionsLayout.LikeAddDbContext<Hand.HandListedRackContext>().UseSqlite(Sqlite).Options);
        using var listed = new Listed.ListedRackContext(OptionsLayout.LikeAddDbContext<Listed.ListedRackContext>().UseSqlite(Sqlite).Options);

        var (before, after) = (hand.Database.GenerateCreateScript(), listed.Database.GenerateCreateScript());

        Assert.Equal(before, after);
        Assert.Contains("\"rack_id\"", after, StringComparison.Ordinal);
        Assert.DoesNotContain("\"position\"", after, StringComparison.Ordinal);
    }

    [Fact]
    public void On_sqlite_the_migration_differ_finds_nothing_between_the_two_either_way()
    {
        using var hand = new Hand.HandListedRackContext(OptionsLayout.LikeAddDbContext<Hand.HandListedRackContext>().UseSqlite(Sqlite).Options);
        using var listed = new Listed.ListedRackContext(OptionsLayout.LikeAddDbContext<Listed.ListedRackContext>().UseSqlite(Sqlite).Options);

        var (forward, back) = ValueObjectMappingDiff.Between(hand, listed);

        Assert.Empty(forward);
        Assert.Empty(back);
    }

    [Fact]
    public void Keeping_the_order_is_one_position_column_on_the_childs_table()
    {
        using var listed = new Listed.ListedRackContext(OptionsLayout.LikeAddDbContext<Listed.ListedRackContext>().UseSqlite(Sqlite).Options);
        using var ordered = new Listed.OrderedRackContext(OptionsLayout.LikeAddDbContext<Listed.OrderedRackContext>().UseSqlite(Sqlite).Options);

        var operations = ordered.GetService<IMigrationsModelDiffer>().GetDifferences(
            listed.GetService<IDesignTimeModel>().Model.GetRelationalModel(),
            ordered.GetService<IDesignTimeModel>().Model.GetRelationalModel());

        var added = Assert.IsType<AddColumnOperation>(Assert.Single(operations));
        Assert.Equal(("shelves", "position", typeof(int)), (added.Table, added.Name, added.ClrType));
    }
}
