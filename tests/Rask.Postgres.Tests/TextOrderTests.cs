using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
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

public sealed class Ledger
{
    public string Code { get; set; } = "";

    public string Title { get; set; } = "";
}

public sealed class Posting
{
    public int Id { get; set; }

    public string LedgerCode { get; set; } = "";

    public string Memo { get; set; } = "";
}

/// <summary>Text keys: a ledger keyed by its code, and postings that reference it by that code.</summary>
public sealed class HungarianLedgers(DbContextOptions<HungarianLedgers> options) : DbContext(options)
{
    public DbSet<Ledger> Ledgers => Set<Ledger>();

    public DbSet<Posting> Postings => Set<Posting>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        configurationBuilder.OrderTextBy("Hungarian_100_CI_AS");

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Ledger>().HasKey(l => l.Code);
        modelBuilder.Entity<Posting>().HasOne<Ledger>().WithMany().HasForeignKey(p => p.LedgerCode);
    }
}

/// <summary>The same model under a collation that tells case and accents apart.</summary>
public sealed class CaseSensitivePayees(DbContextOptions<CaseSensitivePayees> options) : DbContext(options)
{
    public DbSet<Payee> Payees => Set<Payee>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        configurationBuilder.OrderTextBy("Hungarian_100_CS_AS");
}

// OrderTextBy on PostgreSQL: through UseRaskPostgres a case-insensitive collation is a nondeterministic ICU one its
// migrations create; a plain UseNpgsql orders by the language's ICU collation and changes no column.
public sealed class TextOrderTests
{
    internal static readonly IServiceProvider Services = new ConfigurationServices(new ConfigurationBuilder()
        .AddInMemoryCollection([new KeyValuePair<string, string?>("Rask:ConnectionStrings:App", "Host=localhost;Database=rask")])
        .Build());

    [Fact]
    public void Through_UseRaskPostgres_every_text_column_is_created_with_the_case_insensitive_collation()
    {
        using var context = new HungarianPayees(new DbContextOptionsBuilder<HungarianPayees>().UseRaskPostgres(Services).Options);

        var script = context.Database.GenerateCreateScript();

        Assert.Contains("CREATE COLLATION IF NOT EXISTS rask_text (PROVIDER = icu, LOCALE = 'hu-HU-u-ks-level2', DETERMINISTIC = false);", script, StringComparison.Ordinal);
        Assert.Contains("server_version_num", script, StringComparison.Ordinal);
        Assert.Contains("\"Name\" text COLLATE rask_text NOT NULL", script, StringComparison.Ordinal);
        Assert.True(
            script.IndexOf("CREATE COLLATION", StringComparison.Ordinal) < script.IndexOf("CREATE TABLE", StringComparison.Ordinal));
    }

    [Fact]
    public void Through_UseRaskPostgres_a_text_key_and_the_foreign_key_to_it_are_binary_collated()
    {
        using var context = new HungarianLedgers(new DbContextOptionsBuilder<HungarianLedgers>().UseRaskPostgres(Services).Options);

        var script = context.Database.GenerateCreateScript();

        Assert.Contains("\"Code\" text COLLATE \"C\" NOT NULL", script, StringComparison.Ordinal);
        Assert.Contains("\"LedgerCode\" text COLLATE \"C\" NOT NULL", script, StringComparison.Ordinal);
        Assert.Contains("\"Title\" text COLLATE rask_text NOT NULL", script, StringComparison.Ordinal);
        Assert.Contains("\"Memo\" text COLLATE rask_text NOT NULL", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Through_UseRaskPostgres_text_in_the_app_s_order_is_ordered_by_that_collation()
    {
        using var context = new HungarianPayees(new DbContextOptionsBuilder<HungarianPayees>().UseRaskPostgres(Services).Options);

        var sql = context.Payees.OrderBy(p => p.Name.InTextOrder()).ToQueryString();

        Assert.Contains("ORDER BY p.\"Name\" COLLATE rask_text", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void A_case_and_accent_sensitive_collation_needs_no_collation_of_its_own()
    {
        using var context = new CaseSensitivePayees(new DbContextOptionsBuilder<CaseSensitivePayees>().UseRaskPostgres(Services).Options);

        var script = context.Database.GenerateCreateScript();
        var sql = context.Payees.OrderBy(p => p.Name.InTextOrder()).ToQueryString();

        Assert.DoesNotContain("COLLATE", script, StringComparison.Ordinal);
        Assert.Contains("ORDER BY p.\"Name\" COLLATE \"hu-HU-x-icu\"", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Through_a_plain_UseNpgsql_text_in_the_app_s_order_is_ordered_by_the_language_s_ICU_collation()
    {
        using var context = Plain();

        var sql = context.Payees.Where(p => p.Name == "Csaba").OrderBy(p => p.Name.InTextOrder()).ToQueryString();

        Assert.Contains("ORDER BY p.\"Name\" COLLATE \"hu-HU-x-icu\"", sql, StringComparison.Ordinal);
        Assert.Contains("WHERE p.\"Name\" = 'Csaba'", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Through_a_plain_UseNpgsql_no_column_is_given_a_collation()
    {
        using var context = Plain();

        var script = context.Database.GenerateCreateScript();

        Assert.DoesNotContain("COLLATE", script, StringComparison.Ordinal);
    }

    private static HungarianPayees Plain() =>
        new(new DbContextOptionsBuilder<HungarianPayees>().UseNpgsql("Host=localhost;Database=rask").Options);

    // Just the configuration: that is all UseRaskPostgres asks the provider for.
    private sealed class ConfigurationServices(IConfiguration configuration) : IServiceProvider
    {
        public object? GetService(Type serviceType) => serviceType == typeof(IConfiguration) ? configuration : null;
    }
}
