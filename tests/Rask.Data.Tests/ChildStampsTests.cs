using Microsoft.EntityFrameworkCore;

namespace Rask.Data.Tests;

/// <summary>A root that implements ITimestamps, so it carries both — and its children carry none.</summary>
public sealed class Roster : Aggregate<Guid>, ITimestamps, IVersioned, IFormModel
{
    private readonly List<RosterSlot> _slots = [];
    private readonly List<RosterBadge> _badges = [];

    private Roster() { }

    public string Name { get; private set; } = "";

    public IReadOnlyList<RosterSlot> Slots => _slots;

    public IReadOnlyList<RosterBadge> Badges => _badges;

    public static Roster Named(string name, string slot, string note, string badge)
    {
        var roster = new Roster { Id = Guid.NewGuid(), Name = name };
        roster._slots.Add(RosterSlot.Holding(slot, note));
        roster._badges.Add(RosterBadge.Called(badge));
        return roster;
    }

    public void Open(string slot) => _slots.Add(RosterSlot.Holding(slot, "new"));

    public void Close(string slot) => _slots.RemoveAll(s => s.Label == slot);
}

/// <summary>A child that names no timestamps, so it has none.</summary>
public sealed class RosterSlot : Entity<Guid>
{
    private readonly List<SlotNote> _notes = [];

    private RosterSlot() { }

    public string Label { get; private set; } = "";

    public IReadOnlyList<SlotNote> Notes => _notes;

    public void Relabel(string label) => Label = label;

    internal static RosterSlot Holding(string label, string note)
    {
        var slot = new RosterSlot { Id = Guid.NewGuid(), Label = label };
        slot._notes.Add(SlotNote.Saying(note));
        return slot;
    }
}

/// <summary>A grandchild that names none either.</summary>
public sealed class SlotNote : Entity<Guid>
{
    private SlotNote() { }

    public string Text { get; private set; } = "";

    internal static SlotNote Saying(string text) => new() { Id = Guid.NewGuid(), Text = text };
}

/// <summary>A child that opts in, which it keeps.</summary>
public sealed class RosterBadge : Entity<Guid>, ITimestamps
{
    private RosterBadge() { }

    public string Title { get; private set; } = "";

    internal static RosterBadge Called(string title) => new() { Id = Guid.NewGuid(), Title = title };
}

/// <summary>
/// Timestamps are the aggregate's, like its tenant: a child carries none unless it declares its own, and a change to
/// any child moves the root's <c>UpdatedAt</c> — so it means "anything in the aggregate changed". A child is written
/// only through its aggregate.
/// </summary>
[Collection(DataDbCollection.Name)]
public sealed class ChildStampsTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-child-stamps-{Guid.NewGuid():N}.db");
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

    public void Dispose() => File.Delete(_dbPath);

    [Fact]
    public async Task A_child_and_grandchild_that_declare_nothing_map_no_timestamps_while_the_root_maps_both()
    {
        await using var database = await StartDatabaseAsync();

        var model = database.Context.Model;

        foreach (var type in new[] { typeof(RosterSlot), typeof(SlotNote) })
        {
            Assert.Null(model.FindEntityType(type)!.FindProperty(Columns.CreatedAt));
            Assert.Null(model.FindEntityType(type)!.FindProperty(Columns.UpdatedAt));
        }

        Assert.NotNull(model.FindEntityType(typeof(Roster))!.FindProperty(Columns.CreatedAt));
        Assert.NotNull(model.FindEntityType(typeof(Roster))!.FindProperty(Columns.UpdatedAt));
    }

    [Fact]
    public async Task A_child_that_declares_its_timestamps_keeps_and_stamps_them()
    {
        await using var database = await StartDatabaseAsync();
        database.Context.Add(Roster.Named("night", "gate", "bring keys", "lead"));

        await database.Context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var badge = await database.Context.Set<RosterBadge>().SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(_clock.UtcNow.UtcDateTime, badge.CreatedAt);
        Assert.Equal(_clock.UtcNow.UtcDateTime, badge.UpdatedAt);
    }

    [Fact]
    public async Task The_root_is_stamped_and_its_children_read_through_their_faces()
    {
        await using var database = await StartDatabaseAsync();
        var id = await SeedAsync(database);

        var roster = (await database.Load<Roster>(id, TestContext.Current.CancellationToken))!;

        Assert.Equal(_clock.UtcNow.UtcDateTime, roster.CreatedAt);
        Assert.Equal(["gate"], await RosterSlot.Select(s => s.Label));
        Assert.Equal(["bring keys"], await SlotNote.Select(n => n.Text));
    }

    [Theory]
    [InlineData("modify")]
    [InlineData("add")]
    [InlineData("remove")]
    public async Task Changing_only_a_child_moves_the_roots_UpdatedAt(string change)
    {
        await using var database = await StartDatabaseAsync();
        var id = await SeedAsync(database);
        _clock.UtcNow = _clock.UtcNow.AddHours(1);

        await Roster.Update(
            id,
            r =>
            {
                switch (change)
                {
                    case "modify": r.Slots[0].Relabel("door"); break;
                    case "add": r.Open("door"); break;
                    default: r.Close("gate"); break;
                }
            },
            cancellationToken: TestContext.Current.CancellationToken);

        var after = (await database.Load<Roster>(id, TestContext.Current.CancellationToken))!;
        Assert.Equal(_clock.UtcNow.UtcDateTime, after.UpdatedAt);
    }

    [Fact]
    public async Task A_save_that_changes_nothing_leaves_the_roots_UpdatedAt()
    {
        await using var database = await StartDatabaseAsync();
        var id = await SeedAsync(database);
        var stamped = _clock.UtcNow.UtcDateTime;
        _clock.UtcNow = _clock.UtcNow.AddHours(1);

        await Roster.Update(id, _ => { }, cancellationToken: TestContext.Current.CancellationToken);

        var after = (await database.Load<Roster>(id, TestContext.Current.CancellationToken))!;
        Assert.Equal(stamped, after.UpdatedAt);
    }

    [Fact]
    public async Task A_child_changed_alone_is_refused_naming_its_root_even_across_tenants()
    {
        await using var database = await StartDatabaseAsync();
        await SeedAsync(database);
        var slot = await database.Context.Set<RosterSlot>().FirstAsync(TestContext.Current.CancellationToken);
        slot.Relabel("door");

        InvalidOperationException refused;
        using (Tenant.Across())
        {
            refused = await Assert.ThrowsAsync<InvalidOperationException>(
                () => database.Context.SaveChangesAsync(TestContext.Current.CancellationToken));
        }

        Assert.Contains("'RosterSlot' was changed on its own", refused.Message, StringComparison.Ordinal);
        Assert.Contains("'Roster'", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_child_removed_alone_is_refused_and_stays()
    {
        await using var database = await StartDatabaseAsync();
        await SeedAsync(database);
        var note = await database.Context.Set<SlotNote>().FirstAsync(TestContext.Current.CancellationToken);
        database.Context.Remove(note);

        var refused = await Assert.ThrowsAsync<InvalidOperationException>(
            () => database.Context.SaveChangesAsync(TestContext.Current.CancellationToken));

        Assert.Contains("'SlotNote' was deleted on its own", refused.Message, StringComparison.Ordinal);
        database.Context.ChangeTracker.Clear();
        Assert.Equal(1, await database.Context.Set<SlotNote>().CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_bulk_insert_of_children_alone_is_refused_naming_their_root()
    {
        await using var database = await StartDatabaseAsync();

        var refused = await Assert.ThrowsAsync<InvalidOperationException>(
            () => database.Context.BulkInsert(
                [RosterSlot.Holding("gate", "x")], cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("'Roster'", refused.Message, StringComparison.Ordinal);
        Assert.Equal(0, await database.Context.Set<RosterSlot>().CountAsync(TestContext.Current.CancellationToken));
    }

    private static async Task<Guid> SeedAsync(TestDatabase database)
    {
        var roster = Roster.Named("night", "gate", "bring keys", "lead");
        database.Context.Add(roster);
        await database.Context.SaveChangesAsync(TestContext.Current.CancellationToken);
        database.Context.ChangeTracker.Clear();
        return roster.Id;
    }

    private Task<TestDatabase> StartDatabaseAsync() =>
        TestDatabase.Start(o => o.UseSqlite($"Data Source={_dbPath}"), _clock);
}
