using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Rask.Providers.E2E.Tests;

/// <summary>A convoy's id: a version-7 Guid, made by the app.</summary>
public readonly partial record struct ConvoyId(Guid Value);

/// <summary>A convoy, keyed by its typed id, with a ticket of a plain Guid and an optional one.</summary>
public sealed class Convoy : Aggregate<ConvoyId>
{
    private Convoy() { } // EF materialization

    public string Name { get; private set; } = "";

    public Guid Ticket { get; private set; }

    public Guid? Spare { get; private set; }

    public static Convoy Named(ConvoyId id, string name, Guid ticket, Guid? spare) =>
        new() { Id = id, Name = name, Ticket = ticket, Spare = spare };
}

/// <summary>A leg of a convoy: a foreign key over the typed id, and a plain Guid key.</summary>
public sealed class ConvoyLeg : Aggregate<Guid>
{
    private ConvoyLeg() { } // EF materialization

    public ConvoyId Convoy { get; private set; }

    public int Km { get; private set; }

    public static ConvoyLeg Of(ConvoyId convoy, int km) => new() { Id = Guid.CreateVersion7(), Convoy = convoy, Km = km };
}

public sealed class ConvoyDbContext(DbContextOptions<ConvoyDbContext> options) : DbContext(options)
{
    public const string Schema = "rask_guid_order";

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        ModelRegistry.ApplyConventions(configurationBuilder);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Database.IsSqlite() ? null : Schema);
        modelBuilder.Entity<Convoy>();
        modelBuilder.Entity<ConvoyLeg>().HasOne<Convoy>().WithMany().HasForeignKey(l => l.Convoy);
        modelBuilder.ApplyRaskConventions(this);
    }
}

/// <summary>
///     The same answers about Guid keys on SQLite, SQL Server and PostgreSQL — and the order .NET's
///     <see cref="Guid.CompareTo(Guid)" /> gives: on SQL Server because Rask stores every uniqueidentifier time-ordered.
///     Ids from many milliseconds and many within one (a typed id's <c>New()</c> counts within its millisecond),
///     written out of order, half through EF's change tracker and half through <c>BulkInsert</c>.
/// </summary>
public abstract class GuidOrderParityTests(ProviderLeg leg) : ParityTests<ConvoyDbContext>(leg)
{
    private const int PageSize = 37;

    // 150 ids a millisecond apart, then 250 made in one tight loop: most share a millisecond and differ by the counter.
    private static readonly List<ConvoyId> Ids =
    [
        .. Enumerable.Range(0, 150).Select(static i => new ConvoyId(Guid.CreateVersion7(DateTimeOffset.UnixEpoch.AddMilliseconds(1_790_000_000_000L + i)))),
        .. Enumerable.Range(0, 250).Select(static _ => ConvoyId.Create()),
    ];

    private static List<ConvoyId> Ordered => [.. Ids.Order()];

    [Fact]
    public async Task Ordering_by_the_key_is_the_order_NET_sorts_the_ids_both_ways()
    {
        RequireServer();
        await SeedAsync();
        await using var db = Open();

        var ascending = await db.Set<Convoy>().OrderBy(c => c.Id).Select(c => c.Id).ToListAsync(Ct);
        var descending = await db.Set<Convoy>().OrderByDescending(c => c.Id).Select(c => c.Id).ToListAsync(Ct);
        var byTicket = await db.Set<Convoy>().OrderBy(c => c.Ticket).Select(c => c.Ticket).ToListAsync(Ct);

        Assert.Equal(Ordered, ascending);
        Assert.Equal(Enumerable.Reverse(Ordered), descending);
        Assert.Equal(byTicket.Order().ToList(), byTicket);
    }

    [Fact]
    public async Task Paging_after_the_last_key_seen_walks_every_row_once_in_order()
    {
        RequireServer();
        await SeedAsync();
        await using var db = Open();
        var walked = await db.Set<Convoy>().OrderBy(c => c.Id).Select(c => c.Id).Take(PageSize).ToListAsync(Ct);

        for (var page = walked; page.Count > 0; walked.AddRange(page))
        {
            var last = walked[^1];
            page = await db.Set<Convoy>().Where(c => c.Id > last)
                .OrderBy(c => c.Id).Select(c => c.Id).Take(PageSize).ToListAsync(Ct);
        }

        Assert.Equal(Ordered, walked);
    }

    [Fact]
    public async Task The_first_and_last_keys_are_the_smallest_and_largest_id()
    {
        RequireServer();
        await SeedAsync();
        await using var db = Open();

        var first = await db.Set<Convoy>().OrderBy(c => c.Id).Select(c => c.Id).FirstAsync(Ct);
        var last = await db.Set<Convoy>().OrderBy(c => c.Id).Select(c => c.Id).LastAsync(Ct);
        var fifthNewest = Ordered[^5];
        var newest = await db.Set<Convoy>().Where(c => c.Id >= fifthNewest).CountAsync(Ct);

        Assert.Equal(Ids.Min(), first);
        Assert.Equal(Ids.Max(), last);
        Assert.Equal(5, newest);
    }

    [Fact]
    public async Task Min_and_Max_of_the_key_are_the_smallest_and_largest_id()
    {
        RequireServer();
        await SeedAsync();
        await using var db = Open();

        var min = await db.Set<Convoy>().MinAsync(c => c.Id, Ct);
        var max = await db.Set<Convoy>().MaxAsync(c => c.Id, Ct);

        Assert.Equal(Ids.Min(), min);
        Assert.Equal(Ids.Max(), max);
    }

    [Fact]
    public async Task A_key_is_found_by_equality_by_a_list_and_through_its_foreign_keys()
    {
        RequireServer();
        await SeedAsync();
        await using var db = Open();
        var some = Ids.Where(static (_, i) => i % 7 == 0).ToList();
        var one = Ids[123];
        var ticket = (await db.Set<Convoy>().AsNoTracking().SingleAsync(c => c.Id == one, Ct)).Ticket;

        var found = await db.Set<Convoy>().Where(c => c.Id == one).Select(c => c.Name).SingleAsync(Ct);
        var byTicket = await db.Set<Convoy>().Where(c => c.Ticket == ticket).Select(c => c.Id).SingleAsync(Ct);
        var listed = await db.Set<Convoy>().Where(c => some.Contains(c.Id)).Select(c => c.Id).ToListAsync(Ct);
        var legs = await db.Set<ConvoyLeg>().CountAsync(l => l.Convoy == one, Ct);
        var joined = await db.Set<ConvoyLeg>()
            .Join(db.Set<Convoy>(), l => l.Convoy, c => c.Id, (l, c) => new { c.Id, c.Name })
            .Where(x => x.Id == one).Select(x => x.Name).ToListAsync(Ct);

        Assert.Equal("convoy 123", found);
        Assert.Equal(one, byTicket);
        Assert.Equal(some.Order().ToList(), listed.Order().ToList());
        Assert.Equal(2, legs);
        Assert.Equal(["convoy 123", "convoy 123"], joined);
    }

    [Fact]
    public async Task Grouping_by_a_Guid_column_counts_each_key_s_rows()
    {
        RequireServer();
        await SeedAsync();
        await using var db = Open();

        var groups = await db.Set<ConvoyLeg>().GroupBy(l => l.Convoy)
            .Select(g => new { g.Key, Count = g.Count(), Km = g.Sum(l => l.Km) })
            .ToListAsync(Ct);

        Assert.Equal(Ids.Count, groups.Count);
        Assert.All(groups, static g => Assert.Equal(2, g.Count));
        Assert.Equal(Ordered, groups.Select(static g => g.Key).Order().ToList());
        Assert.Equal(Ids.Count * 3, groups.Sum(static g => g.Km));
    }

    [Fact]
    public async Task An_id_reads_back_as_the_same_lower_case_text_and_json()
    {
        RequireServer();
        await SeedAsync();
        await using var db = Open();

        var read = await db.Set<Convoy>().AsNoTracking().OrderBy(c => c.Id).ToListAsync(Ct);

        Assert.Equal(Ordered.Select(static id => id.Value.ToString()), read.Select(static c => c.Id.ToString()));
        Assert.All(read, static c => Assert.Equal(c.Id.ToString().ToLowerInvariant(), c.Id.ToString()));
        Assert.Equal(
            JsonSerializer.Serialize(Ordered),
            JsonSerializer.Serialize(read.Select(static c => c.Id)));
    }

    private protected override ConvoyDbContext Open() => Leg.Open<ConvoyDbContext>(o => new ConvoyDbContext(o));

    // Out of order: half through the change tracker, half through BulkInsert; two legs a convoy.
    private async Task SeedAsync()
    {
        var shuffled = Ids.Select(static (id, i) => (Id: id, Name: $"convoy {i}"))
            .OrderBy(static x => x.Id.Value.GetHashCode()).ToList();
        var convoys = shuffled.Select(static (x, i) =>
            Convoy.Named(x.Id, x.Name, i % 3 == 0 ? Guid.NewGuid() : Guid.CreateVersion7(), i % 2 == 0 ? null : Guid.CreateVersion7())).ToList();

        await using (var db = Open())
        {
            db.Set<Convoy>().AddRange(convoys.Take(convoys.Count / 2));
            await db.SaveChangesAsync(Ct);
        }

        await using (var db = Open())
        {
            await db.BulkInsert(convoys.Skip(convoys.Count / 2), cancellationToken: Ct);
            await db.BulkInsert(shuffled.SelectMany(static x => new[] { ConvoyLeg.Of(x.Id, 1), ConvoyLeg.Of(x.Id, 2) }), cancellationToken: Ct);
        }
    }
}

public sealed class SqliteGuidOrderParityTests() : GuidOrderParityTests(ProviderLeg.Sqlite("guid-order"));

[Collection(SqlServerCollection.Name)]
public sealed class SqlServerGuidOrderParityTests() : GuidOrderParityTests(ProviderLeg.SqlServer("rask_e2e_guid_order"));

[Collection(PostgresCollection.Name)]
public sealed class PostgresGuidOrderParityTests() : GuidOrderParityTests(ProviderLeg.Postgres(ConvoyDbContext.Schema));
