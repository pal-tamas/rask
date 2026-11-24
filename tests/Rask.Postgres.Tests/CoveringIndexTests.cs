using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Rask.Data;

namespace Rask.Postgres.Tests;

/// <summary>A payment, read by its payee with its amount — what a covering index is for.</summary>
public sealed class Remittance
{
    public Guid Id { get; set; }

    public Guid PayeeId { get; set; }

    public decimal Amount { get; set; }

    public string Note { get; set; } = "";
}

/// <summary>The payee's index carrying the amount and the note.</summary>
public sealed class CoveredContext(DbContextOptions<CoveredContext> options) : RaskDbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<Remittance>().HasIndex(r => r.PayeeId).Covering(r => new { r.Amount, r.Note });
    }
}

/// <summary><c>Covering(…)</c> on PostgreSQL, model only: Npgsql's own included columns.</summary>
public sealed class CoveringIndexTests
{
    [Fact]
    public void A_covering_index_carries_its_columns_as_PostgreSQL_s_included_ones()
    {
        using var context = new CoveredContext(Options());

        var model = context.GetService<IDesignTimeModel>().Model;

        var index = model.FindEntityType(typeof(Remittance))!.GetIndexes().Single();
        Assert.Equal(["Amount", "Note"], index.GetIncludeProperties());
        Assert.Null(index.FindAnnotation("Rask:Covering"));
    }

    [Fact]
    public void The_schema_creates_the_index_over_its_key_including_the_covered_columns()
    {
        using var context = new CoveredContext(Options());

        var script = context.Database.GenerateCreateScript();

        Assert.Contains("ON \"Remittance\" (\"PayeeId\") INCLUDE (\"Amount\", \"Note\")", script, StringComparison.Ordinal);
    }

    private static DbContextOptions<CoveredContext> Options() =>
        new DbContextOptionsBuilder<CoveredContext>().UseNpgsql("Host=unused;Database=unused").Options;
}
