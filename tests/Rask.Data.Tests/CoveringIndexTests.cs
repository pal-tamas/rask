using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Rask.Data.Tests;

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

/// <summary>An index covering a member the entity does not map.</summary>
public sealed class CoveringUnmappedContext(DbContextOptions<CoveringUnmappedContext> options) : RaskDbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<Remittance>().HasIndex(r => r.PayeeId).Covering("Memo");
    }
}

/// <summary>An index covering its own key.</summary>
public sealed class CoveringItsKeyContext(DbContextOptions<CoveringItsKeyContext> options) : RaskDbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<Remittance>().HasIndex(r => r.PayeeId).Covering(r => r.PayeeId);
    }
}

/// <summary><c>Covering(…)</c> on SQLite, which has no included columns: the index is its keys, and nothing else is said.</summary>
[Collection(DataDbCollection.Name)]
public sealed class CoveringIndexTests
{
    [Fact]
    public void On_SQLite_a_covering_index_is_its_keys_alone_and_the_model_says_nothing_of_the_rest()
    {
        using var context = new CoveredContext(Options<CoveredContext>());

        var model = context.GetService<IDesignTimeModel>().Model;

        var index = model.FindEntityType(typeof(Remittance))!.GetIndexes().Single();
        Assert.Equal([nameof(Remittance.PayeeId)], index.Properties.Select(p => p.Name));
        Assert.DoesNotContain(index.GetAnnotations(), a => a.Name.Contains("Include", StringComparison.Ordinal)
                                                           || a.Name.StartsWith("Rask:", StringComparison.Ordinal));
    }

    [Fact]
    public void On_SQLite_the_schema_creates_the_index_over_its_key_column_only()
    {
        using var context = new CoveredContext(Options<CoveredContext>());

        var script = context.Database.GenerateCreateScript();

        Assert.Contains("ON \"Remittance\" (\"PayeeId\");", script, StringComparison.Ordinal);
        Assert.DoesNotContain("INCLUDE", script, StringComparison.Ordinal);
    }

    [Fact]
    public void A_covered_member_the_entity_does_not_map_fails_the_model_with_its_name()
    {
        using var context = new CoveringUnmappedContext(Options<CoveringUnmappedContext>());

        var refused = Assert.Throws<InvalidOperationException>(() => context.Model);

        Assert.Contains("covers Memo, which is not one of its mapped properties", refused.Message, StringComparison.Ordinal);
        Assert.Contains("Remittance(PayeeId)", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_covered_member_that_is_already_a_key_fails_the_model_with_its_name()
    {
        using var context = new CoveringItsKeyContext(Options<CoveringItsKeyContext>());

        var refused = Assert.Throws<InvalidOperationException>(() => context.Model);

        Assert.Contains("covers PayeeId, which is already one of its keys", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Covering_nothing_is_refused_where_it_is_written()
    {
        var builder = new ModelBuilder().Entity<Remittance>().HasIndex(r => r.PayeeId);

        var refused = Assert.Throws<ArgumentException>(() => ((Microsoft.EntityFrameworkCore.Metadata.Builders.IndexBuilder)builder).Covering());

        Assert.Equal("propertyNames", refused.ParamName);
    }

    private static DbContextOptions<TContext> Options<TContext>()
        where TContext : DbContext =>
        OptionsLayout.LikeAddDbContext<TContext>().UseSqlite("Data Source=:memory:").Options;
}
