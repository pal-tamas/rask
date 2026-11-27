using Microsoft.EntityFrameworkCore;
using Rask.Data.Tests.Shapes;
using Hand = Rask.Data.Tests.Shapes.HandListed;
using Listed = Rask.Data.Tests.Shapes.Listed;

namespace Rask.Providers.E2E.Tests;

/// <summary>
/// The no-migration condition for moving an aggregate's children from a private <c>List</c> and its view to one
/// <c>HasMany</c>, on the two server databases: the same create script, and nothing for EF Core's migration differ.
/// </summary>
/// <remarks>Proved without a server — a model needs a provider but no connection.</remarks>
public sealed class HasManyMappingDiffTests
{
    private const string OfflineSqlServer = "Server=(local);Database=rask_model_only;Trusted_Connection=True";
    private const string OfflinePostgres = "Host=localhost;Database=rask_model_only";

    [Fact]
    public void On_sql_server_a_hand_written_child_list_and_a_HasMany_are_the_same_schema()
    {
        using var hand = new Hand.HandListedRackContext(new DbContextOptionsBuilder<Hand.HandListedRackContext>().UseSqlServer(OfflineSqlServer).Options);
        using var listed = new Listed.ListedRackContext(new DbContextOptionsBuilder<Listed.ListedRackContext>().UseSqlServer(OfflineSqlServer).Options);

        var (forward, back) = ValueObjectMappingDiff.Between(hand, listed);

        Assert.Empty(forward);
        Assert.Empty(back);
        Assert.Equal(hand.Database.GenerateCreateScript(), listed.Database.GenerateCreateScript());
    }

    [Fact]
    public void On_postgresql_a_hand_written_child_list_and_a_HasMany_are_the_same_schema()
    {
        using var hand = new Hand.HandListedRackContext(new DbContextOptionsBuilder<Hand.HandListedRackContext>().UseNpgsql(OfflinePostgres).Options);
        using var listed = new Listed.ListedRackContext(new DbContextOptionsBuilder<Listed.ListedRackContext>().UseNpgsql(OfflinePostgres).Options);

        var (forward, back) = ValueObjectMappingDiff.Between(hand, listed);

        Assert.Empty(forward);
        Assert.Empty(back);
        Assert.Equal(hand.Database.GenerateCreateScript(), listed.Database.GenerateCreateScript());
    }
}
