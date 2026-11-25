using Microsoft.EntityFrameworkCore;

namespace Rask.Providers.E2E.Tests;

/// <summary>A stop on a trip: a value, so the collection of them is one JSON column.</summary>
public sealed record TripStop(string City, int Day);

/// <summary>
/// An aggregate holding both shapes of value collection, in the encapsulated form Rask recommends: a private
/// field and a read-only view.
/// </summary>
public sealed class Trip : Aggregate<Guid, Timestamps, Versioned, Model, Deletable>
{
    private readonly List<string> _tags = [];
    private readonly List<TripStop> _stops = [];

    private Trip() { }

    public string Name { get; private set; } = "";

    public IReadOnlyList<string> Tags => _tags;

    public IReadOnlyList<TripStop> Stops => _stops;

    public static Trip Called(string name) => new() { Id = Guid.NewGuid(), Name = name };

    public void Tag(string tag) => _tags.Add(tag);

    public void StopAt(string city, int day) => _stops.Add(new TripStop(city, day));
}

public sealed class TripDbContext(DbContextOptions<TripDbContext> options) : DbContext(options)
{
    public const string Schema = "value_collections";

    public DbSet<Trip> Trips => Set<Trip>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Its own schema, because these tests share one PostgreSQL database with every other class here.
        modelBuilder.Entity<Trip>().ToTable("Trip", Database.IsSqlite() ? null : Schema);
        modelBuilder.ApplyRaskConventions();
    }
}

/// <summary>
/// The two value-collection mappings, written and filtered in the database. The per-provider translation
/// (<c>json_each</c>, <c>OPENJSON</c>, <c>jsonb</c>) is EF Core's, not SQL Rask writes, so the round trip runs on SQLite
/// alone; the container column's type is held on every server by <see cref="JsonColumnParityTests" />, and the SQL
/// Server model and translation without a server by <see cref="SqlServerValueCollectionTranslationTests" />.
/// </summary>
/// <remarks>
/// Every filter runs in the database: since EF Core 3 a <c>Where</c> it cannot translate THROWS rather than falling back
/// to the client, so awaiting one at all proves the translation, and its answer proves the translation is right.
/// PostgreSQL maps the primitive collection to a native <c>text[]</c> (<c>'urgent' = ANY (t."Tags")</c>) and the value
/// objects to <c>jsonb</c> read through <c>jsonb_to_recordset</c>; SQLite uses <c>json_each</c> for both; SQL Server
/// <c>OPENJSON</c>.
/// </remarks>
public abstract class ValueCollectionParityTests(ProviderLeg leg) : ParityTests<TripDbContext>(leg)
{
    [Fact]
    public async Task Both_value_collections_come_back_as_the_domain_methods_left_them()
    {
        RequireServer();
        await SeedAsync();

        await using var db = Open();
        var loaded = await db.Trips.SingleAsync(t => t.Name == "grand tour", Ct);

        Assert.Equal(["urgent"], loaded.Tags);
        Assert.Equal([("Vienna", 1), ("Prague", 2)], loaded.Stops.Select(s => (s.City, s.Day)));
    }

    [Fact]
    public async Task A_primitive_collection_is_searched_in_the_database()
    {
        RequireServer();
        await SeedAsync();

        await using var db = Open();
        var urgent = await db.Trips.Where(t => t.Tags.Contains("urgent")).Select(t => t.Name).SingleAsync(Ct);
        var calm = await db.Trips.Where(t => t.Tags.Contains("calm")).Select(t => t.Name).SingleAsync(Ct);

        Assert.Equal("grand tour", urgent);
        Assert.Equal("weekend", calm);
    }

    [Fact]
    public async Task A_document_of_value_objects_is_searched_by_its_members_in_the_database()
    {
        RequireServer();
        await SeedAsync();

        await using var db = Open();
        var byText = await db.Trips.Where(t => t.Stops.Any(s => s.City == "Vienna")).Select(t => t.Name).SingleAsync(Ct);
        var byNumber = await db.Trips.Where(t => t.Stops.Any(s => s.Day >= 2)).Select(t => t.Name).SingleAsync(Ct);
        var counted = await db.Trips.OrderBy(t => t.Name).Select(t => t.Stops.Count).ToListAsync(Ct);

        Assert.Equal("grand tour", byText);
        Assert.Equal("grand tour", byNumber);
        Assert.Equal([2, 1], counted);
    }

    private protected override TripDbContext Open() => Leg.Open<TripDbContext>(o => new TripDbContext(o));

    private async Task SeedAsync()
    {
        var trip = Trip.Called("grand tour");
        trip.Tag("urgent");
        trip.StopAt("Vienna", 1);
        trip.StopAt("Prague", 2);

        var other = Trip.Called("weekend");
        other.Tag("calm");
        other.StopAt("Rome", 1);

        await using var db = Open();
        db.AddRange(trip, other);
        await db.SaveChangesAsync(Ct);
    }
}

public sealed class SqliteValueCollectionTests() : ValueCollectionParityTests(ProviderLeg.Sqlite("value-collections"));

/// <summary>
/// SQL Server, proved without a server: the model builds and both filters translate. The queries themselves are EF
/// Core's translation, not SQL Rask writes, so they run on SQLite alone (<see cref="SqliteValueCollectionTests" />).
/// </summary>
public sealed class SqlServerValueCollectionTranslationTests
{
    private const string Offline = "Server=(local);Database=rask_translation_only;Trusted_Connection=True";

    [Fact]
    public void Both_value_collections_map_and_translate()
    {
        using var db = new TripDbContext(
            new DbContextOptionsBuilder<TripDbContext>().UseRaskSqlServerAt(Offline).Options);
        var trip = db.Model.FindEntityType(typeof(Trip))!;

        // A primitive collection is a column; a collection of value objects is a complex collection held as JSON.
        Assert.True(trip.FindProperty(nameof(Trip.Tags))!.IsPrimitiveCollection);
        Assert.True(trip.FindComplexProperty(nameof(Trip.Stops))!.ComplexType.IsMappedToJson());

        // Both filters reach the column rather than coming back as client-side evaluation.
        Assert.Contains("OPENJSON", db.Trips.Where(t => t.Tags.Contains("urgent")).ToQueryString(), StringComparison.Ordinal);
        Assert.Contains("OPENJSON", db.Trips.Where(t => t.Stops.Any(s => s.City == "Vienna")).ToQueryString(), StringComparison.Ordinal);
    }
}
