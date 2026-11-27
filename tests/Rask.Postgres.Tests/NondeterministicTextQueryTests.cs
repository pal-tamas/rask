using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Rask.Data;

namespace Rask.Postgres.Tests;

public sealed class Contact
{
    public int Id { get; set; }

    public string Name { get; set; } = "";

    public string Code { get; set; } = "";

    public string Raw { get; set; } = "";
}

/// <summary>A case-insensitive, accent-sensitive model, as TA's and Eszaf's are — one column keeping a collation of its own.</summary>
public sealed class HungarianContacts(DbContextOptions<HungarianContacts> options) : DbContext(options)
{
    public DbSet<Contact> Contacts => Set<Contact>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        configurationBuilder.OrderTextBy("Hungarian_100_CI_AS");

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<Contact>().Property(c => c.Raw).UseCollation("C");
}

/// <summary>A collation that tells case apart but not accents: nondeterministic too, and case-sensitive.</summary>
public sealed class AccentBlindContacts(DbContextOptions<AccentBlindContacts> options) : DbContext(options)
{
    public DbSet<Contact> Contacts => Set<Contact>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        configurationBuilder.OrderTextBy("Hungarian_100_CS_AI");
}

// The string operations PostgreSQL refuses on a nondeterministic collation (strpos, replace, the regular expressions,
// ILIKE), as UseRaskPostgres writes them on a column in rask_text: the SQL alone — the servers run in the provider suite.
public sealed class NondeterministicTextQueryTests
{
    private static IServiceProvider Services => TextOrderTests.Services;

    [Fact]
    public void Contains_StartsWith_and_EndsWith_of_a_parameter_stay_an_escaped_LIKE()
    {
        using var context = Hungarian();
        var typed = "ko%v";

        var contains = context.Contacts.Where(c => c.Name.Contains(typed)).ToQueryString();
        var starts = context.Contacts.Where(c => c.Name.StartsWith(typed)).ToQueryString();

        Assert.Contains("@typed_contains='%ko\\%v%'", contains, StringComparison.Ordinal);
        Assert.Contains("WHERE c.\"Name\" LIKE @typed_contains", contains, StringComparison.Ordinal);
        Assert.Contains("WHERE c.\"Name\" LIKE @typed_startswith", starts, StringComparison.Ordinal);
        Assert.DoesNotContain("strpos", contains, StringComparison.Ordinal);
    }

    [Fact]
    public void Contains_of_another_column_is_a_LIKE_by_the_column_s_collation_with_the_value_escaped()
    {
        using var context = Hungarian();

        var sql = context.Contacts.Where(c => c.Name.Contains(c.Code)).ToQueryString();

        Assert.Contains(
            "WHERE c.\"Name\" LIKE '%' || (replace(replace(replace(c.\"Code\" COLLATE \"hu-HU-x-icu\", '\\', '\\\\'), '%', '\\%'), '_', '\\_') COLLATE rask_text) || '%' ESCAPE '\\'",
            sql,
            StringComparison.Ordinal);
        Assert.DoesNotContain("strpos", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void StartsWith_and_EndsWith_of_another_column_are_a_LIKE_not_left_or_right()
    {
        using var context = Hungarian();

        var starts = context.Contacts.Where(c => c.Name.StartsWith(c.Code)).ToQueryString();
        var ends = context.Contacts.Where(c => c.Name.EndsWith(c.Code)).ToQueryString();

        Assert.Contains("LIKE (replace(", starts, StringComparison.Ordinal);
        Assert.Contains("'_', '\\_') COLLATE rask_text) || '%' ESCAPE '\\'", starts, StringComparison.Ordinal);
        Assert.Contains("LIKE '%' || (replace(", ends, StringComparison.Ordinal);
        Assert.Contains("'_', '\\_') COLLATE rask_text) ESCAPE '\\'", ends, StringComparison.Ordinal);
        Assert.DoesNotContain("left(", starts, StringComparison.Ordinal);
        Assert.DoesNotContain("right(", ends, StringComparison.Ordinal);
    }

    [Fact]
    public void IndexOf_finds_the_value_case_aside_by_the_language_s_deterministic_collation()
    {
        using var context = Hungarian();
        var typed = "KOV";

        var sql = context.Contacts.Select(c => c.Name.IndexOf(typed)).ToQueryString();

        Assert.Contains(
            "SELECT strpos(lower(c.\"Name\" COLLATE \"hu-HU-x-icu\"), lower(@typed COLLATE \"hu-HU-x-icu\")) - 1",
            sql,
            StringComparison.Ordinal);
    }

    [Fact]
    public void IndexOf_under_a_case_sensitive_collation_compares_case_too()
    {
        using var context = new AccentBlindContacts(
            new DbContextOptionsBuilder<AccentBlindContacts>().UseRaskPostgres(Services).Options);

        var sql = context.Contacts.Select(c => c.Name.IndexOf(c.Code)).ToQueryString();

        Assert.Contains("SELECT strpos(c.\"Name\" COLLATE \"hu-HU-x-icu\", c.\"Code\" COLLATE \"hu-HU-x-icu\") - 1", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Replace_runs_by_the_deterministic_collation_and_its_answer_compares_by_the_column_s_again()
    {
        using var context = Hungarian();
        var typed = "a";

        var sql = context.Contacts.Where(c => c.Name.Replace(typed, "b") == "KOVÁCS").ToQueryString();

        Assert.Contains("WHERE replace(c.\"Name\" COLLATE \"hu-HU-x-icu\", @typed, 'b') COLLATE rask_text = 'KOVÁCS'", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void A_regular_expression_and_ILike_run_by_the_deterministic_collation()
    {
        using var context = Hungarian();

        var regex = context.Contacts.Where(c => Regex.IsMatch(c.Name, "^Kov")).ToQueryString();
        var ilike = context.Contacts.Where(c => EF.Functions.ILike(c.Name, "kov%")).ToQueryString();

        Assert.Contains("(c.\"Name\" COLLATE \"hu-HU-x-icu\") ~ '(?p)^Kov'", regex, StringComparison.Ordinal);
        Assert.Contains("c.\"Name\" COLLATE \"hu-HU-x-icu\" ILIKE 'kov%'", ilike, StringComparison.Ordinal);
    }

    [Fact]
    public void A_column_with_a_deterministic_collation_of_its_own_is_left_to_Npgsql()
    {
        using var context = Hungarian();

        var contains = context.Contacts.Where(c => c.Raw.Contains(c.Raw)).ToQueryString();
        var index = context.Contacts.Select(c => c.Raw.IndexOf("x")).ToQueryString();

        Assert.Contains("strpos(c.\"Raw\", c.\"Raw\") > 0", contains, StringComparison.Ordinal);
        Assert.Contains("strpos(c.\"Raw\", 'x') - 1", index, StringComparison.Ordinal);
    }

    [Fact]
    public void Through_a_plain_UseNpgsql_the_string_operations_are_Npgsql_s()
    {
        using var context = new HungarianContacts(
            new DbContextOptionsBuilder<HungarianContacts>().UseNpgsql("Host=localhost;Database=rask").Options);

        var sql = context.Contacts.Where(c => c.Name.Contains(c.Code)).ToQueryString();

        Assert.Contains("strpos(c.\"Name\", c.\"Code\") > 0", sql, StringComparison.Ordinal);
    }

    private static HungarianContacts Hungarian() =>
        new(new DbContextOptionsBuilder<HungarianContacts>().UseRaskPostgres(Services).Options);
}
