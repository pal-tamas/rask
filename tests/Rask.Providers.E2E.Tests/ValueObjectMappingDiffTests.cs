using Microsoft.EntityFrameworkCore;
using Rask.Data.Tests.Shapes;

namespace Rask.Providers.E2E.Tests;

/// <summary>
/// The no-migration condition for mapping a one-value value object as a converted scalar, on the two server
/// databases: EF Core's migration differ finds nothing between the old complex mapping and the new one.
/// </summary>
/// <remarks>
/// Proved without a server — the differ compares two models, and a model needs a provider but no connection.
/// </remarks>
public sealed class ValueObjectMappingDiffTests
{
    private const string OfflineSqlServer = "Server=(local);Database=rask_model_only;Trusted_Connection=True";
    private const string OfflinePostgres = "Host=localhost;Database=rask_model_only";

    [Fact]
    public void On_sql_server_moving_between_the_two_mappings_is_no_migration_either_way()
    {
        using var complex = new ComplexShapeContext(new DbContextOptionsBuilder<ComplexShapeContext>().UseSqlServer(OfflineSqlServer).Options);
        using var scalar = new ScalarShapeContext(new DbContextOptionsBuilder<ScalarShapeContext>().UseSqlServer(OfflineSqlServer).Options);

        var (forward, back) = ValueObjectMappingDiff.Between(complex, scalar);

        Assert.Empty(forward);
        Assert.Empty(back);
        Assert.Equal(ValueObjectMappingDiff.Columns(complex), ValueObjectMappingDiff.Columns(scalar));
    }

    [Fact]
    public void On_postgresql_moving_between_the_two_mappings_is_no_migration_either_way()
    {
        using var complex = new ComplexShapeContext(new DbContextOptionsBuilder<ComplexShapeContext>().UseNpgsql(OfflinePostgres).Options);
        using var scalar = new ScalarShapeContext(new DbContextOptionsBuilder<ScalarShapeContext>().UseNpgsql(OfflinePostgres).Options);

        var (forward, back) = ValueObjectMappingDiff.Between(complex, scalar);

        Assert.Empty(forward);
        Assert.Empty(back);
        Assert.Equal(ValueObjectMappingDiff.Columns(complex), ValueObjectMappingDiff.Columns(scalar));
    }

    [Fact]
    public void The_columns_are_the_ones_each_inner_type_and_its_facets_ask_for()
    {
        using var scalar = new ScalarShapeContext(new DbContextOptionsBuilder<ScalarShapeContext>().UseSqlServer(OfflineSqlServer).Options);

        var columns = ValueObjectMappingDiff.Columns(scalar);

        Assert.Equal(
            [
                "Count int NOT NULL default=-",
                "Day date NOT NULL default=-",
                "Id int NOT NULL default=-",
                "Label nvarchar(255) NOT NULL default=-",
                "Note nvarchar(max) NULL default=-",
                "Price decimal(18,9) NOT NULL default=-",
                "Ref uniqueidentifier NOT NULL default=-",
                "TenantId int NULL default=-",
            ],
            columns);
    }
}
