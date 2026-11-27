using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Rask.Background;
using Rask.Data;

namespace Rask.SqlServer.Tests;

/// <summary>A plain <c>DbContext</c> holding Rask's job tables, as an app may host a battery.</summary>
public sealed class PlainJobsContext(DbContextOptions<PlainJobsContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder) => modelBuilder.AddRaskJobs();
}

/// <summary>
///     The time-ordered Guid columns on every <c>UseRaskSqlServer</c> context — a plain <c>DbContext</c> that never asked
///     for Rask's model conventions too — and on none a plain <c>UseSqlServer</c> opens.
/// </summary>
public sealed class PlainContextGuidOrderTests
{
    [Fact]
    public void A_plain_context_on_UseRaskSqlServer_stores_the_Guids_of_the_battery_tables_it_holds_by_time()
    {
        using var context = new PlainJobsContext(new DbContextOptionsBuilder<PlainJobsContext>().UseRaskSqlServer(Configuration()).Options);
        var job = context.Model.FindEntityType(typeof(Job))!;
        var id = Guid.CreateVersion7();

        var stored = job.FindProperty(nameof(Job.ClaimToken))!.GetTypeMapping().Converter?.ConvertToProvider(id);
        var order = context.GetService<IDesignTimeModel>().Model.FindAnnotation(SqlServerGuidOrder.Annotation)?.Value;

        Assert.Equal(SqlServerGuidOrder.ToStore(id), stored);
        Assert.Equal(SqlServerGuidOrder.ByTime, order);
    }

    [Fact]
    public void A_plain_context_on_UseSqlServer_keeps_SQL_Servers_own_layout()
    {
        using var context = new PlainJobsContext(new DbContextOptionsBuilder<PlainJobsContext>().UseSqlServer("Server=unused;Database=unused").Options);
        var job = context.Model.FindEntityType(typeof(Job))!;

        var converter = job.FindProperty(nameof(Job.ClaimToken))!.GetTypeMapping().Converter;
        var order = context.GetService<IDesignTimeModel>().Model.FindAnnotation(SqlServerGuidOrder.Annotation);

        Assert.Null(converter);
        Assert.Null(order);
    }

    [Fact]
    public void Rasks_own_context_is_stored_by_one_convention_and_carries_no_mark_into_its_migrations()
    {
        using var context = new RaskAppDbContext(new DbContextOptionsBuilder<RaskAppDbContext>().UseRaskSqlServer(Configuration()).Options);
        var job = context.Model.FindEntityType(typeof(Job))!;
        var id = Guid.CreateVersion7();

        var stored = job.FindProperty(nameof(Job.ClaimToken))!.GetTypeMapping().Converter?.ConvertToProvider(id);
        var annotations = context.GetService<IDesignTimeModel>().Model.GetAnnotations().Select(static a => a.Name);

        Assert.Equal(SqlServerGuidOrder.ToStore(id), stored);
        Assert.DoesNotContain("Rask:ModelConventions", annotations);
    }

    private static ConfigurationServices Configuration() => ConfigurationServices.With(new()
    {
        ["Rask:ConnectionStrings:App"] = "Server=unused;Database=unused",
        ["Rask:SqlServer:CompatibilityLevel"] = "160",
    });
}
