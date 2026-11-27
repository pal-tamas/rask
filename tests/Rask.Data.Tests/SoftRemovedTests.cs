using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Rask.Data.Tests;

/// <summary>A hangar whose stalls are let go of, never deleted: what a stall recorded stays, and its name stays taken.</summary>
public sealed class Hangar : Aggregate<Guid>, IVersioned
{
    private readonly List<Stall> _stalls = [];

    private Hangar() { }

    public string Name { get; private set; } = "";

    public IReadOnlyList<Stall> Stalls => _stalls;

    public static Hangar Named(string name, params string[] stalls)
    {
        var hangar = new Hangar { Id = Guid.NewGuid(), Name = name };
        hangar._stalls.AddRange(stalls.Select(Stall.Called));
        return hangar;
    }

    public void Open(string stall) => _stalls.Add(Stall.Called(stall));

    public void Close(string stall) => _stalls.RemoveAll(b => b.Name == stall);
}

/// <summary>A child that says ISoftDeletes.</summary>
public sealed class Stall : Entity<Guid>, ISoftDeletes
{
    public const string Taken = "A stall with this name already exists.";

    private readonly List<StallMark> _marks = [];

    private Stall() { }

    public string Name { get; private set; } = "";

    public IReadOnlyList<StallMark> Marks => _marks;

    internal static Stall Called(string name)
    {
        var stall = new Stall { Id = Guid.NewGuid(), Name = name };
        stall._marks.Add(StallMark.Saying("painted"));
        return stall;
    }

    public static void Configure(EntityTypeBuilder<Stall> builder) => builder.HasIndex(b => b.Name).IsUnique(Taken);
}

/// <summary>What a stall holds, kept with it.</summary>
public sealed class StallMark : Entity<Guid>
{
    private StallMark() { }

    public string Text { get; private set; } = "";

    internal static StallMark Saying(string text) => new() { Id = Guid.NewGuid(), Text = text };
}

/// <summary>
/// F-30: a child that names <see cref="ISoftDeletes" /> is kept, stamped, when its aggregate lets go of it — hidden from
/// the aggregate's load and from its read face, listed again by IgnoreQueryFilters, its unique name still taken.
/// </summary>
public sealed class SoftRemovedTests : IDisposable
{
    private readonly InMemorySqlite _sqlite = new();
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 3, 1, 8, 0, 0, TimeSpan.Zero));

    public void Dispose() => _sqlite.Dispose();

    [Fact]
    public async Task A_soft_removed_child_maps_a_DeletedAt_and_the_soft_delete_filter()
    {
        await using var database = await StartDatabaseAsync();

        var stall = database.Context.Model.FindEntityType(typeof(Stall))!;

        Assert.NotNull(stall.FindProperty(Columns.DeletedAt));
        Assert.Contains(stall.GetDeclaredQueryFilters(), f => f.Key == "SoftDelete");
        Assert.Null(database.Context.Model.FindEntityType(typeof(StallMark))!.FindProperty(Columns.DeletedAt));
    }

    [Fact]
    public async Task A_child_let_go_of_keeps_its_row_stamped_and_what_it_holds()
    {
        await using var database = await StartDatabaseAsync();
        var id = await SeedAsync(database);

        await ChangeAsync(id, d => d.Close("north"));

        database.Context.ChangeTracker.Clear();
        var kept = await database.Context.Set<Stall>().IgnoreQueryFilters()
            .SingleAsync(b => b.Name == "north", TestContext.Current.CancellationToken);
        Assert.Equal(_clock.UtcNow.UtcDateTime, database.Context.Entry(kept).Property<DateTime?>(Columns.DeletedAt).CurrentValue);
        Assert.Equal(2, await database.Context.Set<StallMark>().CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task The_aggregates_load_and_the_childs_read_face_no_longer_see_it()
    {
        await using var database = await StartDatabaseAsync();
        var id = await SeedAsync(database);

        await ChangeAsync(id, d => d.Close("north"));

        var hangar = (await database.Load<Hangar>(id, TestContext.Current.CancellationToken))!;
        Assert.Equal(["south"], hangar.Stalls.Select(b => b.Name));
        Assert.Equal(["south"], await Stall.Select(b => b.Name));
        Assert.Equal(["north", "south"], (await Stall.IgnoreQueryFilters().Select(b => b.Name)).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task The_name_a_removed_child_held_stays_taken()
    {
        await using var database = await StartDatabaseAsync();
        var id = await SeedAsync(database);
        await ChangeAsync(id, d => d.Close("north"));

        var refused = await Assert.ThrowsAsync<RaskValidationException>(
            () => ChangeAsync(id, d => d.Open("north")));

        Assert.Contains(Stall.Taken, refused.Errors.Values.SelectMany(static m => m));
    }

    private static async Task<Guid> SeedAsync(TestDatabase database)
    {
        var hangar = Hangar.Named("main", "north", "south");
        database.Context.Add(hangar);
        await database.Context.SaveChangesAsync(TestContext.Current.CancellationToken);
        database.Context.ChangeTracker.Clear();
        return hangar.Id;
    }

    private static async Task ChangeAsync(Guid id, Action<Hangar> change)
    {
        var hangar = (await Hangar.Find(id, TestContext.Current.CancellationToken))!;
        change(hangar);
        await hangar.Save(cancellationToken: TestContext.Current.CancellationToken);
    }

    private Task<TestDatabase> StartDatabaseAsync() =>
        TestDatabase.Start(o => o.UseSqlite(_sqlite.ConnectionString), _clock);
}
