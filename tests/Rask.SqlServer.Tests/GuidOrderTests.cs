using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Rask.Background;
using Rask.Data;

namespace Rask.SqlServer.Tests;

/// <summary>
///     Rask's own context on <c>UseRaskSqlServer</c>, as TA's and Eszaf's are opened: its uniqueidentifier columns are
///     stored by time. The rewrite of an existing database is proved in Rask.Providers.E2E.Tests (GuidOrderModelTests).
/// </summary>
public sealed class GuidOrderTests
{
    [Fact]
    public void Every_uniqueidentifier_column_of_Rask_s_own_tables_is_stored_by_time()
    {
        using var context = new RaskAppDbContext(Options());
        var job = context.Model.FindEntityType(typeof(Job))!;
        var id = Guid.CreateVersion7();

        object? Stored(string property) => job.FindProperty(property)!.GetTypeMapping().Converter?.ConvertToProvider(id);

        Assert.Equal(SqlServerGuidOrder.ByTime, context.GetService<IDesignTimeModel>().Model.FindAnnotation(SqlServerGuidOrder.Annotation)?.Value);
        Assert.Equal(SqlServerGuidOrder.ToStore(id), Stored(nameof(Job.TenantId)));
        Assert.Equal(SqlServerGuidOrder.ToStore(id), Stored(nameof(Job.UserId)));
        Assert.Equal(SqlServerGuidOrder.ToStore(id), Stored(nameof(Job.Batch)));
        Assert.Equal(SqlServerGuidOrder.ToStore(id), Stored(nameof(Job.ClaimToken)));
        Assert.Equal(id, job.FindProperty(nameof(Job.TenantId))!.GetTypeMapping().Converter!.ConvertFromProvider(SqlServerGuidOrder.ToStore(id)));
    }

    [Fact]
    public void A_database_its_first_migration_creates_is_written_in_the_stored_form_from_the_start()
    {
        using var ordered = new RaskAppDbContext(Options());

        var operations = ordered.GetService<IMigrationsModelDiffer>()
            .GetDifferences(null, ordered.GetService<IDesignTimeModel>().Model.GetRelationalModel());

        Assert.DoesNotContain(operations, static o => o is SqlOperation);
    }

    // UseRaskSqlServer, as an app opens it.
    private static DbContextOptions<RaskAppDbContext> Options() =>
        new DbContextOptionsBuilder<RaskAppDbContext>().UseRaskSqlServer(ConfigurationServices.With(new()
        {
            ["Rask:ConnectionStrings:App"] = "Server=unused;Database=unused",
            ["Rask:SqlServer:CompatibilityLevel"] = "160",
        })).Options;
}
