using Microsoft.EntityFrameworkCore;

namespace Rask.Data.Tests;

// An aggregate that declares nothing but its own state: the framework's columns come from the bases.
public sealed class Memo : Aggregate<Guid>
{
    // Soft delete is opt-in now: this aggregate is one whose tests are about it.
    public const Deletion Deletes = Deletion.Soft;

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
    public async Task An_aggregate_that_says_nothing_deletes_for_real()
    {
        await using var database = await StartDatabaseAsync();

        // The default is HARD: no column, no query filter, and DeleteAsync removes the row. An aggregate
        // that wants the row kept says so — see Memo, which declares Deletes = Deletion.Soft.
        var receipt = database.Context.Model.FindEntityType(typeof(Receipt))!;
        Assert.Null(receipt.FindProperty(Columns.DeletedAt));
        Assert.Empty(receipt.GetDeclaredQueryFilters());

        // And the version token is still there, because that default did NOT move.
        Assert.NotNull(receipt.FindProperty(Columns.Version));

        var kept = await Receipt.CreateAsync(r => r.Note("keep"));
        var gone = await Receipt.CreateAsync(r => r.Note("go"));

        await Receipt.DeleteAsync(gone.Id);

        // Gone means gone: not hidden by a filter, actually absent from the table.
        Assert.Null(await database.LoadAsync<Receipt>(gone.Id));
        Assert.Equal(0, await Receipt.Read.IgnoreQueryFilters().CountAsync(r => r.Id == gone.Id));
        Assert.NotNull(await database.LoadAsync<Receipt>(kept.Id));
    }

    [Fact]
    public async Task An_entity_that_narrows_its_Stamps_maps_only_what_it_asked_for()
    {
        await using var database = await StartDatabaseAsync();

        var ledger = database.Context.Model.FindEntityType(typeof(AppendOnlyLedger))!;

        // The whole point of the const: an append-only table has nothing an UpdatedAt could mean.
        Assert.NotNull(ledger.FindProperty(Columns.CreatedAt));
        Assert.Null(ledger.FindProperty(Columns.UpdatedAt));

        // And a neighbour that says nothing is untouched, so the default is still "both".
        var memo = database.Context.Model.FindEntityType(typeof(Memo))!;
        Assert.NotNull(memo.FindProperty(Columns.CreatedAt));
        Assert.NotNull(memo.FindProperty(Columns.UpdatedAt));
    }

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

        var stored = (await database.LoadAsync<Memo>(memo.Id))!;
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

        Assert.Equal(0, await Memo.Read.CountAsync());
        var deleted = Assert.Single(await Memo.Read.IgnoreQueryFilters().ToListAsync());
        Assert.NotNull(deleted.DeletedAt);
    }

    private Task<TestDatabase> StartDatabaseAsync(TimeProvider? clock = null) =>
        TestDatabase.StartAsync(o => o.UseSqlite($"Data Source={_dbPath}"), clock);
}

/// <summary>An append-only table: written once, never changed, so it declines <c>UpdatedAt</c>.</summary>
public sealed class AppendOnlyLedger : Entity<long>
{
    /// <summary>Only the creation stamp means anything here.</summary>
    public const Timestamps Stamps = Timestamps.Created;

    /// <summary>What was recorded.</summary>
    public string Line { get; private set; } = "";
}

/// <summary>An aggregate that declares nothing, so it takes every default as it now stands.</summary>
public sealed class Receipt : Aggregate<Guid>
{
    /// <summary>What the receipt says.</summary>
    public string Text { get; private set; } = "";

    /// <summary>Sets the text.</summary>
    /// <param name="text">The new text.</param>
    public void Note(string text) => Text = text;
}
