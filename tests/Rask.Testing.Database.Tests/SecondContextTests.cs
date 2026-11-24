using Microsoft.EntityFrameworkCore;
using Rask.Testing.Fixture.Pumps;

namespace Rask.Testing.Database.Tests;

/// <summary>A context of the app's beside its own, reading the tables as the database has them — TA's <c>TaDb</c>.</summary>
public sealed class CompanyTables(DbContextOptions<CompanyTables> options) : DbContext(options)
{
    public DbSet<CompanyRow> Companies => Set<CompanyRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<CompanyRow>().ToTable(nameof(Company));
}

public sealed class CompanyRow
{
    public Guid Id { get; set; }

    public string Name { get; set; } = "";
}

// database.Options<TOther>(): a second context type on the test's own database, with no service provider of its own.
public sealed class SecondContextTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_second_context_type_reads_the_rows_the_fixture_saved()
    {
        await using var database = await TestDatabase.Start<PumpsContext>(cancellationToken: Ct);
        database.Context.Add(Company.Named("Acme"));
        await database.Context.SaveChangesAsync(Ct);

        await using var tables = new CompanyTables(database.Options<CompanyTables>().Options);
        var names = await tables.Companies.Select(c => c.Name).ToListAsync(Ct);

        Assert.Equal(["Acme"], names);
    }
}
