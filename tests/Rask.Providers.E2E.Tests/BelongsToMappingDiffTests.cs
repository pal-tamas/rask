using Microsoft.EntityFrameworkCore;
using Rask.Data.Tests.Shapes;
using Hand = Rask.Data.Tests.Shapes.HandReferenced;
using Referenced = Rask.Data.Tests.Shapes.Referenced;

namespace Rask.Providers.E2E.Tests;

/// <summary>
/// The no-migration condition for moving a hand-written reference to another aggregate — an id, a navigation, a configured
/// <c>HasOne&lt;T&gt;().WithMany()</c> — to one <c>BelongsTo</c>, on the two server databases: the same create script, and
/// nothing for EF Core's migration differ.
/// </summary>
/// <remarks>Proved without a server — a model needs a provider but no connection.</remarks>
public sealed class BelongsToMappingDiffTests
{
    private const string OfflineSqlServer = "Server=(local);Database=rask_model_only;Trusted_Connection=True";
    private const string OfflinePostgres = "Host=localhost;Database=rask_model_only";

    [Fact]
    public void On_sql_server_a_hand_written_reference_and_a_BelongsTo_are_the_same_schema()
    {
        using var hand = new Hand.HandReferencedQuarryContext(new DbContextOptionsBuilder<Hand.HandReferencedQuarryContext>().UseSqlServer(OfflineSqlServer).Options);
        using var referenced = new Referenced.ReferencedQuarryContext(new DbContextOptionsBuilder<Referenced.ReferencedQuarryContext>().UseSqlServer(OfflineSqlServer).Options);

        var (forward, back) = ValueObjectMappingDiff.Between(hand, referenced);

        Assert.Empty(forward);
        Assert.Empty(back);
        Assert.Equal(hand.Database.GenerateCreateScript(), referenced.Database.GenerateCreateScript());
    }

    [Fact]
    public void On_postgresql_a_hand_written_reference_and_a_BelongsTo_are_the_same_schema()
    {
        using var hand = new Hand.HandReferencedQuarryContext(new DbContextOptionsBuilder<Hand.HandReferencedQuarryContext>().UseNpgsql(OfflinePostgres).Options);
        using var referenced = new Referenced.ReferencedQuarryContext(new DbContextOptionsBuilder<Referenced.ReferencedQuarryContext>().UseNpgsql(OfflinePostgres).Options);

        var (forward, back) = ValueObjectMappingDiff.Between(hand, referenced);

        Assert.Empty(forward);
        Assert.Empty(back);
        Assert.Equal(hand.Database.GenerateCreateScript(), referenced.Database.GenerateCreateScript());
    }
}
