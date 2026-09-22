using Microsoft.EntityFrameworkCore;
using Rask.SqlServer;

namespace Rask.Providers.E2E.Tests;

/// <summary>A stop on a trip: a value, so the collection of them is one JSON column.</summary>
public sealed record TripStop(string City, int Day);

/// <summary>
/// An aggregate holding both shapes of value collection, in the encapsulated form Rask recommends: a private
/// field and a read-only view.
/// </summary>
public sealed class Trip : Aggregate<Guid>
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
    public DbSet<Trip> Trips => Set<Trip>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Its own schema, because these tests share one PostgreSQL database with every other class here.
        modelBuilder.Entity<Trip>().ToTable("Trip", "value_collections");
        modelBuilder.ApplyRaskConventions();
    }
}

/// <summary>
/// The two value-collection mappings against every engine Rask supports, because neither is portable by
/// assumption: <c>ToJson()</c> and primitive collections are translated per provider, and SQLite's
/// <c>json_each</c> has nothing to do with SQL Server's <c>OPENJSON</c> or PostgreSQL's <c>jsonb</c>.
/// </summary>
/// <remarks>
/// SQLite's own round trip lives in <c>Rask.Data.Tests</c>, which runs in the unit gate. SQL Server cannot be
/// run on every host (its image is amd64-only and segfaults under emulation on Apple Silicon), so its fact here
/// proves the model builds and the query translates — which is what would break — without needing a server.
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class PostgresValueCollectionTests
{
    private const string Schema = "value_collections";

    [SkippableFact]
    public async Task Both_value_collections_round_trip_and_are_queried_in_sql()
    {
        Skip.IfNot(Postgres.Available, Postgres.SkipReason);

        await using (var db = NewContext())
        {
            await Postgres.ResetSchemaAsync(db, Schema);

            var trip = Trip.Called("grand tour");
            trip.Tag("urgent");
            trip.StopAt("Vienna", 1);
            trip.StopAt("Prague", 2);

            var other = Trip.Called("weekend");
            other.Tag("calm");
            other.StopAt("Rome", 1);

            db.AddRange(trip, other);
            await db.SaveChangesAsync();
        }

        await using (var db = NewContext())
        {
            // The round trip: what the domain methods added is still there, in order.
            var loaded = await db.Trips.SingleAsync(t => t.Name == "grand tour");
            Assert.Equal(["urgent"], loaded.Tags);
            Assert.Equal([("Vienna", 1), ("Prague", 2)], loaded.Stops.Select(s => (s.City, s.Day)));

            // Both filters run in the database: since EF Core 3 a Where it cannot translate THROWS rather
            // than falling back to the client, so awaiting these at all is the proof, and the result is the
            // proof that the translation is also correct.
            //
            // What they translate to differs per engine, which is why this test exists: PostgreSQL maps the
            // primitive collection to a native text[] and filters with `'urgent' = ANY (t."Tags")`, and the
            // value objects to jsonb read back through `jsonb_to_recordset(t."Stops") ... WITH ORDINALITY`.
            // SQLite uses json_each for both; SQL Server uses OPENJSON.
            Assert.Equal("grand tour", (await db.Trips.Where(t => t.Tags.Contains("urgent")).SingleAsync()).Name);
            Assert.Equal("weekend", (await db.Trips.Where(t => t.Tags.Contains("calm")).SingleAsync()).Name);
            Assert.Equal("grand tour", (await db.Trips.Where(t => t.Stops.Any(s => s.City == "Vienna")).SingleAsync()).Name);

            // A non-string member of the document, so the comparison is typed rather than textual.
            Assert.Equal("grand tour", (await db.Trips.Where(t => t.Stops.Any(s => s.Day >= 2)).SingleAsync()).Name);
        }

        await using (var db = NewContext())
        {
            await Postgres.DropSchemaAsync(db, Schema);
        }
    }

    private static TripDbContext NewContext() =>
        new(new DbContextOptionsBuilder<TripDbContext>().UseRaskPostgresAt(Postgres.Required).Options);
}

/// <summary>
/// SQL Server, proved without a server: the model builds and both filters translate. Execution is covered by
/// <c>scripts/run-providers-local.sh</c> on a host that can run the image.
/// </summary>
public sealed class SqlServerValueCollectionTests
{
    private const string Offline = "Server=(local);Database=rask_translation_only;Trusted_Connection=True";

    [Fact]
    public void Both_value_collections_map_and_translate()
    {
        using var db = new TripDbContext(
            new DbContextOptionsBuilder<TripDbContext>().UseRaskSqlServerAt(Offline).Options);
        var trip = db.Model.FindEntityType(typeof(Trip))!;

        // A primitive collection is a column; a collection of value objects is a navigation held as JSON.
        Assert.True(trip.FindProperty(nameof(Trip.Tags))!.IsPrimitiveCollection);
        Assert.True(trip.FindNavigation(nameof(Trip.Stops))!.TargetEntityType.IsMappedToJson());

        // Both filters reach the column rather than coming back as client-side evaluation.
        Assert.Contains("OPENJSON", db.Trips.Where(t => t.Tags.Contains("urgent")).ToQueryString(), StringComparison.Ordinal);
        Assert.Contains("OPENJSON", db.Trips.Where(t => t.Stops.Any(s => s.City == "Vienna")).ToQueryString(), StringComparison.Ordinal);
    }
}
