using Microsoft.EntityFrameworkCore;

namespace Rask.Data.Tests;

// An aggregate that declares nothing but its own state: the framework's columns come from the bases.
public sealed class Memo : Aggregate<Guid>
{
    private Memo() { } // EF materialization

    public string Text { get; private set; } = "";

    public static Memo Write(string text) => new() { Id = Guid.NewGuid(), Text = text };

    public void Edit(string text) => Text = text;
}

// Every Entity<TId> has CreatedAt/UpdatedAt, and every Aggregate<TId> also Version and DeletedAt — declared on the
// bases, readable, and written only by the framework.
[Collection(DataDbCollection.Name)]
public sealed class FrameworkColumnTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-columns-{Guid.NewGuid():N}.db");

    public void Dispose() => File.Delete(_dbPath);

    [Fact]
    public async Task Every_aggregate_maps_the_framework_columns_as_ordinary_properties()
    {
        await using var database = await StartDatabaseAsync();

        var memo = database.Context.Model.FindEntityType(typeof(Memo))!;

        foreach (var column in new[] { Columns.CreatedAt, Columns.UpdatedAt, Columns.DeletedAt, Columns.Version })
        {
            var property = memo.FindProperty(column);
            Assert.NotNull(property);
            Assert.False(property.IsShadowProperty(), $"{column} should be a real property of Memo");
        }

        Assert.True(memo.FindProperty(Columns.Version)!.IsConcurrencyToken);
        Assert.NotNull(memo.GetDeclaredQueryFilters().SingleOrDefault());
    }

    [Fact]
    public async Task An_insert_and_an_update_stamp_the_timestamps_and_bump_the_version()
    {
        var start = new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);
        var clock = new FakeClock(start);
        await using var database = await StartDatabaseAsync(clock);

        var memo = Memo.Write("once");
        database.Context.Add(memo);
        await database.Context.SaveChangesAsync();

        Assert.Equal(start.UtcDateTime, memo.CreatedAt);
        Assert.Equal(start.UtcDateTime, memo.UpdatedAt);
        Assert.Equal(0, memo.Version);

        clock.UtcNow = start.AddHours(1);
        memo.Edit("twice");
        await database.Context.SaveChangesAsync();

        var stored = (await Memo.FindAsync(memo.Id))!;
        Assert.Equal("twice", stored.Text);
        Assert.Equal(start.UtcDateTime, stored.CreatedAt);
        Assert.Equal(start.AddHours(1).UtcDateTime, stored.UpdatedAt);
        Assert.Equal(1, stored.Version);
    }

    [Fact]
    public async Task A_delete_stamps_DeletedAt_and_the_filter_hides_the_row()
    {
        await using var database = await StartDatabaseAsync();

        var memo = Memo.Write("doomed");
        database.Context.Add(memo);
        await database.Context.SaveChangesAsync();

        database.Context.Remove(memo);
        await database.Context.SaveChangesAsync();

        Assert.Equal(0, await Memo.CountAsync());
        var deleted = Assert.Single(await Memo.IgnoreQueryFilters().ToListAsync());
        Assert.NotNull(deleted.DeletedAt);
    }

    private Task<TestDatabase> StartDatabaseAsync(TimeProvider? clock = null) =>
        TestDatabase.StartAsync(o => o.UseSqlite($"Data Source={_dbPath}"), clock);
}
