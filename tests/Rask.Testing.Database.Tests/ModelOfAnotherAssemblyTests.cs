using Microsoft.EntityFrameworkCore;
using Rask.Testing.Fixture.Pumps;

namespace Rask.Testing.Database.Tests;

/// <summary>
///     An app's model is registered by its assembly's module initializer, which runs only once something of that
///     assembly is touched. A test whose first line is <c>TestDatabase.Start&lt;AppContext&gt;</c> has touched nothing
///     of it, and once built a model of none — a database without a table.
/// </summary>
public sealed class ModelOfAnotherAssemblyTests
{
    [Fact]
    public async Task A_database_started_over_a_context_whose_assembly_nothing_touched_has_the_tables_of_that_assembly()
    {
        await using var database = await TestDatabase.Start<PumpsContext>(
            cancellationToken: TestContext.Current.CancellationToken);

        var tables = await database.Context.Database
            .SqlQueryRaw<string>("SELECT name AS Value FROM sqlite_master WHERE type = 'table'")
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Contains("Pump", tables);
    }
}
