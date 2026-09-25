using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Rask.Data.Tests;

/// <summary>A waypoint on a journey: a value, so a collection of them is one JSON column.</summary>
public sealed record Waypoint(string City, int Day);

/// <summary>
/// An aggregate holding both shapes of value collection behind the encapsulation Rask recommends everywhere
/// else: a private field, a read-only view, and a domain method as the only way in.
/// </summary>
public sealed class Journey : Aggregate<Guid>
{
    private readonly List<string> _tags = [];
    private readonly List<Waypoint> _waypoints = [];

    private Journey() { }

    public string Title { get; private set; } = "";

    public IReadOnlyList<string> Tags => _tags;

    public IReadOnlyList<Waypoint> Waypoints => _waypoints;

    // A byte[] is a BLOB, and it implements IEnumerable<byte>: without an explicit carve-out it would be
    // mapped as a collection of bytes and every file column in every app would change shape.
    public byte[] Thumbnail { get; private set; } = [];

    public static Journey Called(string title) => new() { Id = Guid.NewGuid(), Title = title };

    public void Tag(string tag) => _tags.Add(tag);

    public void StopAt(string city, int day) => _waypoints.Add(new Waypoint(city, day));
}

/// <summary>An aggregate that maps its own collection, to prove the convention does not overwrite it.</summary>
public sealed class Itinerary : Aggregate<Guid>
{
    private Itinerary() { }

    public List<string> Legs { get; private set; } = [];

    public static Itinerary New() => new() { Id = Guid.NewGuid() };

    public static void Configure(EntityTypeBuilder<Itinerary> builder) =>
        builder.PrimitiveCollection(nameof(Legs)).HasColumnName("TheLegs");
}

/// <summary>
/// A collection of values is one column — a primitive collection for plain values, a JSON column for value
/// objects — mapped, queryable, and carried on the form model.
/// </summary>
/// <remarks>
/// Both shapes used to fail, and neither failed loudly. A collection of value objects made EF Core take the
/// element for an entity type and refuse the whole model with "requires a primary key to be defined". A
/// collection of plain values behind a read-only view was not mapped at all: green build, no diagnostic, and
/// what a domain method added was gone on the next read.
/// </remarks>
[Collection(DataDbCollection.Name)]
public sealed class ValueCollectionTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-values-{Guid.NewGuid():N}.db");

    public void Dispose() => File.Delete(_dbPath);

    [Fact]
    public async Task Plain_values_are_a_primitive_collection_and_value_objects_are_json()
    {
        await using var database = await StartDatabaseAsync();
        var journey = database.Context.Model.FindEntityType(typeof(Journey))!;

        Assert.True(journey.FindProperty(nameof(Journey.Tags))!.IsPrimitiveCollection);
        Assert.True(journey.FindNavigation(nameof(Journey.Waypoints))!.TargetEntityType.IsMappedToJson());

        // The BLOB stays a BLOB: one column holding bytes, not a collection of them.
        var thumbnail = journey.FindProperty(nameof(Journey.Thumbnail))!;
        Assert.False(thumbnail.IsPrimitiveCollection);
        Assert.Equal(typeof(byte[]), thumbnail.ClrType);
    }

    [Fact]
    public async Task What_a_domain_method_adds_survives_the_round_trip()
    {
        await using var database = await StartDatabaseAsync();

        var journey = Journey.Called("grand tour");
        journey.Tag("urgent");
        journey.StopAt("Vienna", 1);
        journey.StopAt("Prague", 2);
        database.Context.Add(journey);
        await database.Context.SaveChangesAsync();

        await using var fresh = new RaskDbContext(Options());
        var loaded = await fresh.Set<Journey>().SingleAsync();

        Assert.Equal(["urgent"], loaded.Tags);
        Assert.Equal([("Vienna", 1), ("Prague", 2)], loaded.Waypoints.Select(w => (w.City, w.Day)));
    }

    [Fact]
    public async Task Both_collections_are_filtered_in_the_database()
    {
        await using var database = await StartDatabaseAsync();

        var tour = Journey.Called("grand tour");
        tour.Tag("urgent");
        tour.StopAt("Vienna", 1);

        var weekend = Journey.Called("weekend");
        weekend.Tag("calm");
        weekend.StopAt("Rome", 1);

        database.Context.AddRange(tour, weekend);
        await database.Context.SaveChangesAsync();

        // Since EF Core 3 a Where it cannot translate throws rather than falling back to the client, so these
        // running at all is the proof that they run in SQL. On SQLite both become json_each; the per-provider
        // spellings are pinned by Rask.Providers.E2E.Tests.
        Assert.Equal(
            "grand tour",
            (await Journey.Read.AsQueryable().Where(j => j.Tags.Contains("urgent")).SingleAsync()).Title);

        Assert.Equal(
            "weekend",
            (await Journey.Read.AsQueryable().Where(j => j.Waypoints.Any(w => w.City == "Rome")).SingleAsync()).Title);

        // A non-string member of the document, so the comparison is typed rather than textual.
        Assert.Equal(2, await Journey.Read.AsQueryable().CountAsync(j => j.Waypoints.Any(w => w.Day == 1)));
    }

    [Fact]
    public async Task The_form_model_carries_both_and_replaces_them_wholesale()
    {
        await using var database = await StartDatabaseAsync();

        var journey = Journey.Called("grand tour");
        journey.Tag("urgent");
        journey.StopAt("Vienna", 1);
        database.Context.Add(journey);
        await database.Context.SaveChangesAsync();

        // The edit shape, as a form reads it: plain values as themselves, value objects as nested models.
        var model = await Journey.Model(journey.Id);
        Assert.NotNull(model);
        Assert.Equal(["urgent"], model.Tags);
        Assert.Equal([("Vienna", 1)], model.Waypoints.Select(w => (w.City, w.Day)));

        // What the form posts is what the aggregate holds afterwards: a value has no identity, so there is
        // nothing to match a posted row against and the stored collection is replaced, not reconciled.
        model.Tags = ["calm", "slow"];
        model.Waypoints.Add(new JourneyModel.WaypointModel { City = "Prague", Day = 2 });

        await Journey.Update(journey.Id, model);

        await using var fresh = new RaskDbContext(Options());
        var saved = await fresh.Set<Journey>().SingleAsync();

        Assert.Equal(["calm", "slow"], saved.Tags);
        Assert.Equal([("Vienna", 1), ("Prague", 2)], saved.Waypoints.Select(w => (w.City, w.Day)));
    }

    [Fact]
    public async Task An_empty_posted_list_clears_the_collection()
    {
        await using var database = await StartDatabaseAsync();

        var journey = Journey.Called("grand tour");
        journey.Tag("urgent");
        journey.StopAt("Vienna", 1);
        database.Context.Add(journey);
        await database.Context.SaveChangesAsync();

        var model = await Journey.Model(journey.Id);
        model!.Tags = [];
        model.Waypoints = [];

        await Journey.Update(journey.Id, model);

        await using var fresh = new RaskDbContext(Options());
        var saved = await fresh.Set<Journey>().SingleAsync();

        Assert.Empty(saved.Tags);
        Assert.Empty(saved.Waypoints);
    }

    [Fact]
    public async Task The_read_face_carries_both()
    {
        await using var database = await StartDatabaseAsync();

        var journey = Journey.Called("grand tour");
        journey.Tag("urgent");
        journey.StopAt("Vienna", 1);
        database.Context.Add(journey);
        await database.Context.SaveChangesAsync();

        var read = await Journey.Read.Where(j => j.Id == journey.Id).SingleOrDefaultAsync();

        Assert.NotNull(read);
        Assert.Equal(["urgent"], read.Tags);

        // A value object in a collection is NOT flattened the way a single one is: one JSON column has
        // nothing to flatten into, so the face carries the value object's own type.
        Assert.Equal([("Vienna", 1)], read.Waypoints.Select(w => (w.City, w.Day)));
    }

    [Fact]
    public async Task What_the_entity_configured_itself_is_left_alone()
    {
        await using var database = await StartDatabaseAsync();

        var legs = database.Context.Model.FindEntityType(typeof(Itinerary))!.FindProperty(nameof(Itinerary.Legs))!;

        // The convention runs LAST, so mapping over a hand-written Configure would silently undo it.
        Assert.True(legs.IsPrimitiveCollection);
        Assert.Equal("TheLegs", legs.GetColumnName());
    }

    private DbContextOptions<RaskDbContext> Options() =>
        new DbContextOptionsBuilder<RaskDbContext>().UseSqlite($"Data Source={_dbPath}").Options;

    private Task<TestDatabase> StartDatabaseAsync() =>
        TestDatabase.StartAsync(o => o.UseSqlite($"Data Source={_dbPath}"));
}
