using Microsoft.EntityFrameworkCore;

namespace Rask.Data.Tests;

// Product.FindAsync(key): an untracked query by primary key, built from the EF model's key metadata. The
// key parameter is typed object (a key may be composite, and its type cannot be inferred at a static call
// site), so what the compiler cannot check — the count, the nulls, the type — is checked here instead.
[Collection(DataDbCollection.Name)]
public sealed class ModelFindTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"rask-find-{Guid.NewGuid():N}.db");

    public void Dispose() => File.Delete(_dbPath);

    [Fact]
    public async Task Finds_the_row_with_that_key()
    {
        await using var database = await StartDatabaseAsync();
        var (_, bravo) = await SeedAsync(database, Widget.Create("alpha"), Widget.Create("bravo"));

        var found = await Widget.FindAsync(bravo.Id);

        Assert.NotNull(found);
        Assert.Equal("bravo", found.Name);
    }

    [Fact]
    public async Task A_key_no_row_has_is_null()
    {
        await using var database = await StartDatabaseAsync();
        await SeedAsync(database, Widget.Create("alpha"), Widget.Create("bravo"));

        Assert.Null(await Widget.FindAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task A_soft_deleted_row_is_not_found()
    {
        await using var database = await StartDatabaseAsync();
        var (doomed, _) = await SeedAsync(database, Widget.Create("doomed"), Widget.Create("kept"));

        database.Context.Remove(doomed);
        await database.Context.SaveChangesAsync();

        Assert.Null(await Widget.FindAsync(doomed.Id));
        Assert.NotNull(await Widget.IgnoreQueryFilters().FirstOrDefaultAsync(w => w.Id == doomed.Id));
    }

    [Fact]
    public async Task A_strongly_typed_id_is_the_key_it_takes()
    {
        await using var database = await StartDatabaseAsync();
        var gadget = Gadget.Create("typed", "T1", 5m);
        database.Context.Add(gadget);
        await database.Context.SaveChangesAsync();

        Assert.Equal("typed", (await Gadget.FindAsync(gadget.Id))!.Name);
        Assert.Equal("typed", (await Gadget.FindAsync([gadget.Id]))!.Name);
        Assert.Null(await Gadget.FindAsync(new GadgetId(Guid.NewGuid())));

        // The raw Guid underneath is not a GadgetId — the point of having one.
        var error = await Assert.ThrowsAsync<ArgumentException>(() => Gadget.FindAsync(gadget.Id.Value));
        Assert.Contains(nameof(GadgetId), error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Each_find_returns_a_fresh_untracked_copy()
    {
        await using var database = await StartDatabaseAsync();
        var (widget, _) = await SeedAsync(database, Widget.Create("alpha"), Widget.Create("bravo"));

        var first = await Widget.FindAsync(widget.Id);
        var second = await Widget.FindAsync(widget.Id);

        // Two contexts, no identity map shared between them: a change to one copy is invisible to the other.
        Assert.NotSame(first, second);
        first!.Rename("changed-in-memory");
        Assert.Equal("alpha", second!.Name);
        Assert.Equal("alpha", (await Widget.FindAsync(widget.Id))!.Name);
    }

    [Fact]
    public async Task The_wrong_number_of_key_values_is_refused()
    {
        await using var database = await StartDatabaseAsync();

        var tooMany = await Assert.ThrowsAsync<ArgumentException>(() =>
            Widget.FindAsync([Guid.NewGuid(), Guid.NewGuid()]));
        await Assert.ThrowsAsync<ArgumentException>(() => Widget.FindAsync(Array.Empty<object?>()));

        Assert.Contains("1 value(s)", tooMany.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_null_key_is_refused()
    {
        await using var database = await StartDatabaseAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => Widget.FindAsync([null]));
        await Assert.ThrowsAsync<ArgumentNullException>(() => Widget.FindAsync((object)null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => Widget.FindAsync((object?[])null!));
    }

    [Fact]
    public async Task A_key_of_the_wrong_type_is_refused()
    {
        await using var database = await StartDatabaseAsync();

        var error = await Assert.ThrowsAsync<ArgumentException>(() => Widget.FindAsync("not-a-guid"));

        Assert.Contains(nameof(Guid), error.Message, StringComparison.Ordinal);
    }

    private Task<TestDatabase> StartDatabaseAsync() =>
        TestDatabase.StartAsync(o => o.UseSqlite($"Data Source={_dbPath}"));

    private static async Task<(Widget First, Widget Second)> SeedAsync(TestDatabase database, Widget first, Widget second)
    {
        database.Context.AddRange(first, second);
        await database.Context.SaveChangesAsync();
        return (first, second);
    }
}
