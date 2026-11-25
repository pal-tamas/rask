using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.DependencyInjection;

namespace Rask.Data.Tests;

/// <summary>A tripsheet whose every save is audited: an int identity key, a nullable column, and legs of its own.</summary>
public sealed class Tripsheet : Aggregate<int, Versioned, Deletable, Audited>
{
    private readonly List<TripsheetLeg> _legs = [];

    private Tripsheet() { } // EF materialization

    public string Number { get; private set; } = "";

    public int? CityKm { get; private set; }

    public decimal Weight { get; private set; }

    public IReadOnlyCollection<TripsheetLeg> Legs => _legs;

    public static Tripsheet Write(string number, int? cityKm, decimal weight, params decimal[] legs)
    {
        var tripsheet = new Tripsheet { Number = number, CityKm = cityKm, Weight = weight };
        foreach (var litres in legs)
        {
            tripsheet._legs.Add(TripsheetLeg.Of(litres));
        }

        return tripsheet;
    }

    public void Change(int? cityKm) => CityKm = cityKm;

    public void Refill(decimal litres) => _legs.First().Refill(litres);

    public static void Configure(EntityTypeBuilder<Tripsheet> builder) => builder.HasIndex(w => w.Number).IsUnique();
}

public sealed class TripsheetLeg : Entity<int>
{
    private TripsheetLeg() { } // EF materialization

    public decimal Litres { get; private set; }

    public void Refill(decimal litres) => Litres = litres;

    internal static TripsheetLeg Of(decimal litres) => new() { Litres = litres };
}

/// <summary>Writes the values as an older table did: in Hungarian, the names in upper case.</summary>
public sealed class ShoutingAuditFormat : AuditFormat
{
    public override string Value(object value) =>
        value is IFormattable formattable ? formattable.ToString(null, CultureInfo.GetCultureInfo("hu-HU")) : base.Value(value);

    public override string Values(IReadOnlyList<AuditValue> values) =>
        base.Values([.. values.Select(v => v with { Name = v.Name.ToUpperInvariant() })]);
}

/// <summary>
///     <c>Audited</c>: each save of the aggregate leaves one <see cref="AuditEntry" /> in that same save — its children's rows
///     within it, a store-generated key read once the rows went out.
/// </summary>
[Collection(DataDbCollection.Name)]
public sealed class AuditedTests : IDisposable
{
    private readonly InMemorySqlite _sqlite = new();

    public void Dispose() => _sqlite.Dispose();

    [Fact]
    public async Task An_insert_leaves_one_entry_with_every_stored_value_and_the_generated_key()
    {
        await using var database = await StartDatabaseAsync();

        var tripsheet = await SaveAsync(database, Tripsheet.Write("W-1", cityKm: null, 1.5m, 12.5m));

        var entry = Assert.Single(await EntriesAsync(database));
        Assert.Equal(("Tripsheet", AuditAction.Insert, $"Id={tripsheet.Id}", AuditEntry.System), (entry.Aggregate, entry.Action, entry.Key, entry.User));
        Assert.Null(entry.Before);
        Assert.Contains($"Id={tripsheet.Id} || Number=W-1", entry.After, StringComparison.Ordinal);
        Assert.Contains("Weight=1.5", entry.After, StringComparison.Ordinal);
        Assert.Contains($"Legs[0].Id={tripsheet.Legs.Single().Id}", entry.After, StringComparison.Ordinal);
        Assert.Contains("Legs[0].Litres=12.5", entry.After, StringComparison.Ordinal);
        Assert.DoesNotContain("CityKm", entry.After, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_update_records_only_what_changed_a_value_that_became_null_among_them()
    {
        await using var database = await StartDatabaseAsync();
        var id = (await SaveAsync(database, Tripsheet.Write("W-1", cityKm: 40, 1.5m, 12.5m))).Id;

        await Tripsheet.Update(id, w => w.Change(cityKm: null), cancellationToken: TestContext.Current.CancellationToken);

        var entry = (await EntriesAsync(database))[^1];
        Assert.Equal(AuditAction.Update, entry.Action);
        Assert.Equal("CityKm=40 || Version=0", entry.Before);
        Assert.Equal("Version=1", entry.After);
    }

    [Fact]
    public async Task A_child_changed_alone_is_recorded_on_its_aggregate_under_its_place()
    {
        await using var database = await StartDatabaseAsync();
        var id = (await SaveAsync(database, Tripsheet.Write("W-1", cityKm: null, 1.5m, 12.5m))).Id;

        await Tripsheet.Update(id, w => w.Refill(20m), cancellationToken: TestContext.Current.CancellationToken);

        var entry = (await EntriesAsync(database))[^1];
        Assert.Equal((AuditAction.Update, $"Id={id}"), (entry.Action, entry.Key));
        Assert.Contains("Legs[0].Litres=12.5", entry.Before, StringComparison.Ordinal);
        Assert.Contains("Legs[0].Litres=20", entry.After, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_delete_records_what_the_aggregate_held()
    {
        await using var database = await StartDatabaseAsync();
        var id = (await SaveAsync(database, Tripsheet.Write("W-1", cityKm: 40, 1.5m))).Id;

        await Tripsheet.Delete(id, cancellationToken: TestContext.Current.CancellationToken);

        var entry = (await EntriesAsync(database))[^1];
        Assert.Equal((AuditAction.Delete, $"Id={id}"), (entry.Action, entry.Key));
        Assert.Contains("CityKm=40", entry.Before, StringComparison.Ordinal);
        Assert.Null(entry.After);
    }

    [Fact]
    public async Task The_entry_names_the_signed_in_user_and_the_time_of_the_save()
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 11, 24, 13, 5, 0, TimeSpan.Zero));
        await using var database = await StartDatabaseAsync(clock);
        var user = Guid.CreateVersion7();

        using (Current.UseUser(user))
        {
            await SaveAsync(database, Tripsheet.Write("W-1", cityKm: null, 1.5m));
        }

        var entry = Assert.Single(await EntriesAsync(database));
        Assert.Equal((user.ToString(), clock.UtcNow.UtcDateTime), (entry.User, entry.At));
    }

    [Fact]
    public async Task A_save_that_fails_writes_no_entry_and_its_retry_writes_one()
    {
        await using var database = await StartDatabaseAsync();
        await SaveAsync(database, Tripsheet.Write("W-1", cityKm: null, 1.5m));
        var duplicate = Tripsheet.Write("W-1", cityKm: null, 2m);
        database.Context.Add(duplicate);

        await Assert.ThrowsAnyAsync<Exception>(() => database.Context.SaveChangesAsync(TestContext.Current.CancellationToken));
        database.Context.Remove(duplicate);
        await SaveAsync(database, Tripsheet.Write("W-2", cityKm: null, 2m));

        Assert.Equal(["W-1", "W-2"], (await EntriesAsync(database)).Select(e => e.After!.Split(" || ")[1][7..]));
    }

    [Fact]
    public async Task The_app_s_own_format_writes_the_values()
    {
        await using var database = await StartDatabaseAsync(services: s => s.AddSingleton<AuditFormat, ShoutingAuditFormat>());

        await SaveAsync(database, Tripsheet.Write("W-1", cityKm: null, 1.5m));

        var entry = Assert.Single(await EntriesAsync(database));
        Assert.Contains("WEIGHT=1,5", entry.After, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_default_format_writes_in_the_invariant_culture_whatever_the_current_one()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("hu-HU");

        string text;
        try
        {
            text = AuditFormat.Default.Values(
                [new("Weight", 1.5m), new("Note", null), new("At", new DateTime(2026, 11, 24, 13, 5, 0, DateTimeKind.Utc))]);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }

        Assert.Equal("Weight=1.5 || At=2026-11-24T13:05:00.0000000Z", text);
    }

    [Fact]
    public async Task An_aggregate_that_is_not_audited_leaves_no_entry()
    {
        await using var database = await StartDatabaseAsync();

        database.Context.Add(Basket.Open("ada"));
        await database.Context.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.Empty(await EntriesAsync(database));
    }

    private static async Task<Tripsheet> SaveAsync(TestDatabase database, Tripsheet tripsheet)
    {
        database.Context.Add(tripsheet);
        await database.Context.SaveChangesAsync(TestContext.Current.CancellationToken);
        database.Context.ChangeTracker.Clear();
        return tripsheet;
    }

    private static async Task<List<AuditEntry>> EntriesAsync(TestDatabase _)
    {
        await using var db = Db.CreateOwnContext();
        return await db.Set<AuditEntry>().OrderBy(e => e.Id).ToListAsync(TestContext.Current.CancellationToken);
    }

    private Task<TestDatabase> StartDatabaseAsync(TimeProvider? clock = null, Action<IServiceCollection>? services = null) =>
        TestDatabase.Start(o => o.UseSqlite(_sqlite.ConnectionString), clock, services, TestContext.Current.CancellationToken);
}
