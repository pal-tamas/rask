using Microsoft.EntityFrameworkCore;

namespace Rask.Data.Tests;

// b.Projects(g => new { g.Level, g.Tank }, changed => …): a save that adds, removes, or moves a watched
// member of a meter raises one event, made from the rows it changed, as the meter's own.
public sealed class ProjectionTests : IDisposable
{
    private readonly InMemorySqlite _sqlite = new();

    public void Dispose() => _sqlite.Dispose();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Meters_added_in_one_save_raise_one_event_with_no_before()
    {
        await using var database = await StartDatabaseAsync();
        using var events = Events.Fake();

        database.Context.AddRange(LevelMeter.Fitted("North", 10), LevelMeter.Fitted("South", 20));
        await database.Context.SaveChangesAsync(Token);

        var moved = events.Published<MeterLevelsMoved>().Only();
        Assert.Equal([(null, 10), (null, 20)], moved.Levels);
        Assert.Equal(["North", "South"], moved.Tanks);
    }

    [Fact]
    public async Task A_watched_change_raises_the_event_with_the_stored_value_before_and_the_new_one_after()
    {
        await using var database = await StartDatabaseAsync();
        var meter = await SavedAsync(database, LevelMeter.Fitted("North", 10));
        using var events = Events.Fake();

        meter.Read(15);
        meter.Move("South");
        await database.Context.SaveChangesAsync(Token);

        var moved = events.Published<MeterLevelsMoved>().Only();
        Assert.Equal([(10, 15)], moved.Levels);
        Assert.Equal(["North", "South"], moved.Tanks);
    }

    [Fact]
    public async Task A_removed_meter_raises_the_event_with_no_after()
    {
        await using var database = await StartDatabaseAsync();
        var meter = await SavedAsync(database, LevelMeter.Fitted("North", 10));
        using var events = Events.Fake();

        database.Context.Remove(meter);
        await database.Context.SaveChangesAsync(Token);

        Assert.Equal([(10, null)], events.Published<MeterLevelsMoved>().Only().Levels);
    }

    [Fact]
    public async Task A_change_to_an_unwatched_member_raises_nothing()
    {
        await using var database = await StartDatabaseAsync();
        var meter = await SavedAsync(database, LevelMeter.Fitted("North", 10));
        using var events = Events.Fake();

        meter.Rename("Front meter");
        await database.Context.SaveChangesAsync(Token);

        events.Published<MeterLevelsMoved>().None();
    }

    [Fact]
    public async Task A_watched_member_set_to_the_value_it_held_raises_nothing()
    {
        await using var database = await StartDatabaseAsync();
        var meter = await SavedAsync(database, LevelMeter.Fitted("North", 10));
        using var events = Events.Fake();

        meter.Read(10);
        await database.Context.SaveChangesAsync(Token);

        events.Published<MeterLevelsMoved>().None();
    }

    [Fact]
    public async Task A_save_rolled_back_raises_nothing_and_the_event_it_gave_back_is_not_raised_by_a_later_save()
    {
        await using var database = await StartDatabaseAsync();
        var meter = await SavedAsync(database, LevelMeter.Fitted("North", 10));
        using var events = Events.Fake();

        meter.Read(15);
        await using (var transaction = await database.Context.Database.BeginTransactionAsync(Token))
        {
            await database.Context.SaveChangesAsync(Token);
            await transaction.RollbackAsync(Token);
        }

        var heardAfterRollback = events.Published<MeterLevelsMoved>().Count;
        meter.Rename("Front meter");
        await database.Context.SaveChangesAsync(Token);

        Assert.Equal(0, heardAfterRollback);
        events.Published<MeterLevelsMoved>().None();
    }

    [Fact]
    public void Watching_something_other_than_the_rows_own_members_is_refused_naming_the_call()
    {
        var builder = new ModelBuilder().Entity<LevelMeter>();

        var refused = Assert.Throws<ArgumentException>(() =>
            builder.Projects(g => g.Level + 1, _ => (MeterLevelsMoved?)null));

        Assert.Contains(") raising MeterLevelsMoved watches the row's own members", refused.Message, StringComparison.Ordinal);
    }

    private async Task<LevelMeter> SavedAsync(TestDatabase database, LevelMeter meter)
    {
        database.Context.Add(meter);
        await database.Context.SaveChangesAsync(Token);
        return meter;
    }

    private Task<TestDatabase> StartDatabaseAsync() =>
        TestDatabase.Start(o => o.UseSqlite(_sqlite.ConnectionString));
}
