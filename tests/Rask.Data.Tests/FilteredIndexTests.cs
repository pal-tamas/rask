using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Rask.Data.Tests;

/// <summary>An index's <c>Where(…)</c> on SQLite: a partial index, holding the rows the C# keeps.</summary>
public sealed class FilteredIndexTests
{
    [Fact]
    public void A_filtered_unique_index_is_a_partial_index_in_the_schema()
    {
        using var context = new NeutralContext(Options());

        var script = context.Database.GenerateCreateScript();

        Assert.Contains("CREATE UNIQUE INDEX \"uq_partner_grants_partner\" ON \"partner_grants\" (\"partner\") WHERE \"revoked_at\" IS NULL;", script, StringComparison.Ordinal);
    }

    [Fact]
    public void A_constant_is_written_as_its_column_stores_it_and_a_boolean_column_as_itself()
    {
        using var context = new NeutralContext(Options());

        var indexes = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(PartnerGrant))!.GetIndexes().ToList();

        Assert.Contains(indexes, i => i.GetFilter() == "\"active\" = 1 AND \"kind\" = 1");
        Assert.Contains(indexes, i => i.GetFilter() == "\"active\" = 0 AND \"level\" IS NOT NULL AND \"level\" <> 3");
        Assert.DoesNotContain(indexes.SelectMany(i => i.GetAnnotations()), a => a.Name == "Rask:Filter");
    }

    [Fact]
    public async Task A_unique_rule_holds_among_the_rows_the_filter_keeps_only()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var context = new NeutralContext(OptionsLayout.LikeAddDbContext<NeutralContext>().UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        context.AddRange(
            new PartnerGrant { Partner = "p1", RevokedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) },
            new PartnerGrant { Partner = "p1", RevokedAt = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc) },
            new PartnerGrant { Partner = "p1" });
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        context.Add(new PartnerGrant { Partner = "p1" });

        await Assert.ThrowsAnyAsync<Exception>(() => context.SaveChangesAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public void An_or_fails_the_model_since_SQL_Server_cannot_hold_it()
    {
        using var context = RefusedContext.Declaring(m => m.Entity<PartnerGrant>().HasIndex(g => g.Partner).Where(g => g.Active || g.Level == null));

        var refused = Assert.Throws<InvalidOperationException>(() => context.Model);

        Assert.Contains("Where(g => (g.Active OrElse (g.Level == null))) on PartnerGrant", refused.Message, StringComparison.Ordinal);
        Assert.Contains("joins with ||", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Not_equal_on_a_nullable_column_fails_the_model_unless_the_nulls_are_left_out_too()
    {
        using var context = RefusedContext.Declaring(m => m.Entity<PartnerGrant>().HasIndex(g => g.Partner).Where(g => g.Level != 3));

        var refused = Assert.Throws<InvalidOperationException>(() => context.Model);

        Assert.Contains("add && x.Column != null", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Two_columns_compared_fail_the_model()
    {
        using var context = RefusedContext.Declaring(m => m.Entity<PartnerGrant>().HasIndex(g => g.Partner).Where(g => g.Level == (int)g.Kind));

        var refused = Assert.Throws<InvalidOperationException>(() => context.Model);

        Assert.Contains("does not compare one column with a constant", refused.Message, StringComparison.Ordinal);
    }

    private static DbContextOptions<NeutralContext> Options() =>
        OptionsLayout.LikeAddDbContext<NeutralContext>().UseSqlite("Data Source=:memory:").Options;
}
