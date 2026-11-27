using Microsoft.EntityFrameworkCore;

namespace Rask.Postgres.Tests;

public sealed class Ticket
{
    public int Id { get; set; }

    public Guid Code { get; set; }

    public Guid? Spare { get; set; }

    public int Lane { get; set; }
}

public sealed class Tickets(DbContextOptions<Tickets> options) : DbContext(options)
{
    public DbSet<Ticket> All => Set<Ticket>();
}

// Min and Max of a uuid, which PostgreSQL has no aggregate for, as UseRaskPostgres writes them: the SQL alone — the
// provider suite runs them on the servers (GuidOrderParityTests).
public sealed class UuidAggregateTests
{
    [Fact]
    public void Min_and_Max_of_a_uuid_run_over_its_text_compared_byte_by_byte()
    {
        using var context = Open();

        var min = context.All.Select(t => context.All.Min(x => x.Code)).ToQueryString();
        var max = context.All.Select(t => context.All.Max(x => x.Spare)).ToQueryString();

        Assert.Contains("SELECT min(a0.\"Code\"::text COLLATE \"C\")::uuid", min, StringComparison.Ordinal);
        Assert.Contains("SELECT max(a0.\"Spare\"::text COLLATE \"C\")::uuid", max, StringComparison.Ordinal);
    }

    [Fact]
    public void A_group_s_filtered_Min_of_a_uuid_reads_only_the_rows_it_keeps()
    {
        using var context = Open();

        var sql = context.All.GroupBy(t => t.Lane).Select(g => g.Where(t => t.Id > 3).Min(t => t.Code)).ToQueryString();

        Assert.Contains("SELECT min(CAST(CASE\n    WHEN a.\"Id\" > 3 THEN a.\"Code\"\nEND AS text) COLLATE \"C\")::uuid", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Min_of_a_number_is_left_to_Npgsql()
    {
        using var context = Open();

        var sql = context.All.Select(t => context.All.Min(x => x.Lane)).ToQueryString();

        Assert.Contains("min(a0.\"Lane\")", sql, StringComparison.Ordinal);
    }

    private static Tickets Open() =>
        new(new DbContextOptionsBuilder<Tickets>().UseRaskPostgres(TextOrderTests.Services).Options);
}
