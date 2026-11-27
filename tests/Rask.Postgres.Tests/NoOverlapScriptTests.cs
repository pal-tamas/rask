using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Rask.Data;

namespace Rask.Postgres.Tests;

/// <summary>A desk booked for days; a booking without a desk is no desk's.</summary>
public sealed class DeskBooking
{
    public int Id { get; set; }

    public int? DeskId { get; set; }

    public string Floor { get; set; } = "";

    public DateOnly From { get; set; }

    public DateOnly? To { get; set; }

    public bool Cancelled { get; set; }
}

public sealed class DeskBookingContext(DbContextOptions<DeskBookingContext> options) : RaskDbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<DeskBooking>()
            .NoOverlap(b => new { b.Floor, b.DeskId }, b => b.From, b => b.To, "The desk is booked then.")
            .Where(b => !b.Cancelled);
    }
}

/// <summary><c>b.NoOverlap(…)</c> on PostgreSQL, as the schema script writes it: an exclusion constraint.</summary>
public sealed class NoOverlapScriptTests
{
    private static readonly IServiceProvider Services = new ConfigurationServices(new ConfigurationBuilder()
        .AddInMemoryCollection([new KeyValuePair<string, string?>("Rask:ConnectionStrings:App", "Host=localhost;Database=rask")])
        .Build());

    [Fact]
    public void The_schema_holds_the_rule_by_an_exclusion_over_btree_gist()
    {
        using var context = new DeskBookingContext(new DbContextOptionsBuilder<DeskBookingContext>().UseRaskPostgres(Services).Options);

        var script = context.Database.GenerateCreateScript();

        Assert.Contains("CREATE EXTENSION IF NOT EXISTS btree_gist;", script, StringComparison.Ordinal);
        Assert.Contains(
            "ALTER TABLE \"DeskBooking\" ADD CONSTRAINT \"EX_DeskBooking_Floor_DeskId_From\" EXCLUDE USING gist "
            + "(\"Floor\" WITH =, \"DeskId\" WITH =, daterange(\"From\", \"To\", '[)') WITH &&) WHERE (\"Cancelled\" = FALSE);",
            script,
            StringComparison.Ordinal);
        Assert.True(
            script.IndexOf("CREATE TABLE \"DeskBooking\"", StringComparison.Ordinal) < script.IndexOf("EXCLUDE USING gist", StringComparison.Ordinal));
    }

    private sealed class ConfigurationServices(IConfiguration configuration) : IServiceProvider
    {
        public object? GetService(Type serviceType) => serviceType == typeof(IConfiguration) ? configuration : null;
    }
}
