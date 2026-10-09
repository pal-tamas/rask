using Microsoft.EntityFrameworkCore;
using Rask.Data.Tests.Shapes;

namespace Rask.Data.Tests;

/// <summary>
/// A one-value value object mapped as a converted scalar is, to the database, the column it always was: EF
/// Core's own migration differ finds nothing to do between the old complex mapping and the new one.
/// </summary>
/// <remarks>
/// This is the condition the change was allowed on — an existing app needs NO migration — so it is proved by
/// the tool that would write that migration, for a string with a length, an int, a decimal with a precision,
/// a DateOnly, a Guid and a nullable value object.
/// </remarks>
public sealed class ValueObjectMappingDiffTests
{
    [Fact]
    public void On_sqlite_moving_between_the_two_mappings_is_no_migration_either_way()
    {
        using var complex = new ComplexShapeContext(new DbContextOptionsBuilder<ComplexShapeContext>().UseSqlite("Data Source=:memory:").Options);
        using var scalar = new ScalarShapeContext(new DbContextOptionsBuilder<ScalarShapeContext>().UseSqlite("Data Source=:memory:").Options);

        var (forward, back) = ValueObjectMappingDiff.Between(complex, scalar);

        Assert.Empty(forward);
        Assert.Empty(back);
        Assert.Equal(ValueObjectMappingDiff.Columns(complex), ValueObjectMappingDiff.Columns(scalar));
    }

    [Fact]
    public void On_sqlite_only_the_order_of_columns_in_a_fresh_table_differs()
    {
        using var complex = new ComplexShapeContext(new DbContextOptionsBuilder<ComplexShapeContext>().UseSqlite("Data Source=:memory:").Options);
        using var scalar = new ScalarShapeContext(new DbContextOptionsBuilder<ScalarShapeContext>().UseSqlite("Data Source=:memory:").Options);

        var before = ValueObjectMappingDiff.CreateOrder(complex);
        var after = ValueObjectMappingDiff.CreateOrder(scalar);

        // A database that exists keeps the order it has; only a table created from scratch lists them anew.
        Assert.Equal(before.Order(StringComparer.Ordinal), after.Order(StringComparer.Ordinal));
        Assert.Equal(["Id", "TenantId", "Count", "Day", "Label", "Note", "Price", "Ref"], before);
        Assert.Equal("Label", after[1]);
        Assert.Equal("TenantId", after[^1]);
    }
}
