using Microsoft.EntityFrameworkCore;
using Rask.Data;

namespace Rask.Postgres.Tests;

public sealed class Payee
{
    public int Id { get; set; }

    public string Name { get; set; } = "";
}

/// <summary>A model that names how text compares by its SQL Server collation, as an app moved from SQL Server does.</summary>
public sealed class HungarianPayees(DbContextOptions<HungarianPayees> options) : DbContext(options)
{
    public DbSet<Payee> Payees => Set<Payee>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        configurationBuilder.OrderTextBy("Hungarian_100_CI_AS");
}

// OrderTextBy on PostgreSQL: the ORDER BY a query asks for names the language's ICU collation; no column changes.
public sealed class TextOrderTests
{
    [Fact]
    public void Text_in_the_app_s_order_is_ordered_by_the_language_s_ICU_collation()
    {
        using var context = Payees();

        var sql = context.Payees.Where(p => p.Name == "Csaba").OrderBy(p => p.Name.InTextOrder()).ToQueryString();

        Assert.Contains("ORDER BY p.\"Name\" COLLATE \"hu-HU-x-icu\"", sql, StringComparison.Ordinal);
        Assert.Contains("WHERE p.\"Name\" = 'Csaba'", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void No_column_is_given_a_collation()
    {
        using var context = Payees();

        var script = context.Database.GenerateCreateScript();

        Assert.DoesNotContain("COLLATE", script, StringComparison.Ordinal);
    }

    private static HungarianPayees Payees() =>
        new(new DbContextOptionsBuilder<HungarianPayees>().UseNpgsql("Host=localhost;Database=rask").Options);
}
