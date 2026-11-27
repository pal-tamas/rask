using Microsoft.EntityFrameworkCore;
using Rask.Data.Tests.Shapes.Referenced;

namespace Rask.Data.Tests;

// BelongsTo<T>: a reference to another aggregate the row holds by id — read through the other aggregate's read face, by
// Read() or Include, and never loaded, tracked or saved as part of this one.
public sealed class BelongsToTests : IDisposable
{
    private readonly InMemorySqlite _sqlite = new();

    public void Dispose() => _sqlite.Dispose();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public void A_reference_is_the_id_it_was_given_and_nothing_without_one()
    {
        var excavator = NewExcavator("Liebherr", quarry: null);

        var dig = NewDig(excavator.Id, 12m);

        Assert.Equal(excavator.Id, dig.Excavator.Id);
        Assert.Equal(excavator.Id, dig.ExcavatorId);
        Assert.Null(dig.DumpedAt);
        Assert.Equal(dig.Excavator, NewDig(excavator.Id, 3m).Excavator);
    }

    [Fact]
    public async Task Read_answers_the_row_referred_to_through_its_read_face()
    {
        await using var database = await StartDatabaseAsync();
        var (quarry, excavator) = await SeedAsync();
        var dig = NewDig(excavator.Id, 12m);
        dig.Dump(quarry.Id);
        await dig.Save(cancellationToken: Token);

        var stored = (await Dig.Find(dig.Id, Token))!;
        var digger = await stored.Excavator.Read(Token);
        var dumpedAt = await stored.DumpedAt.Read(q => q.Name, Token);

        Assert.Equal("Liebherr", digger!.Make);
        Assert.Equal(quarry.Id, digger.QuarryId);
        Assert.Equal("Gánt", dumpedAt);
    }

    [Fact]
    public async Task Many_references_are_read_at_once_by_id_with_no_entry_for_none()
    {
        await using var database = await StartDatabaseAsync();
        var (_, excavator) = await SeedAsync();
        var other = NewExcavator("Komatsu", quarry: null);
        await other.Save(cancellationToken: Token);
        Dig[] digs = [NewDig(excavator.Id, 1m), NewDig(excavator.Id, 2m), NewDig(other.Id, 3m)];

        var excavators = await digs.Select(d => d.Excavator).Read(Token);
        var dumps = await digs.Select(d => d.DumpedAt).Read(Token);

        Assert.Equal(["Komatsu", "Liebherr"], excavators.Values.Select(e => e.Make).Order(StringComparer.Ordinal));
        Assert.Equal("Komatsu", excavators[other.Id].Make);
        Assert.Empty(dumps);
    }

    [Fact]
    public async Task Include_and_ThenInclude_load_the_aggregate_referred_to_and_its_own_reference()
    {
        await using var database = await StartDatabaseAsync();
        var (quarry, excavator) = await SeedAsync();
        await NewDig(excavator.Id, 12m).Save(cancellationToken: Token);

        var plain = await Dig.All.First(Token);
        var row = await Dig.Include(d => d.Excavator).ThenInclude(e => e.Quarry).First(Token);

        Assert.Null(plain.Excavator);
        Assert.Equal("Liebherr", row.Excavator.Make);
        Assert.Equal(quarry.Id, row.Excavator.Quarry!.Id);
        Assert.Equal("Gánt", row.Excavator.Quarry.Name);
    }

    [Fact]
    public async Task ThenInclude_after_the_rows_that_point_here_loads_a_reference_of_each()
    {
        await using var database = await StartDatabaseAsync();
        var (quarry, excavator) = await SeedAsync();
        var dig = NewDig(excavator.Id, 12m);
        dig.Dump(quarry.Id);
        await dig.Save(cancellationToken: Token);

        var row = await Excavator.Include(e => e.Digs).ThenInclude(d => d.DumpedAt).First(Token);

        Assert.Equal("Gánt", Assert.Single(row.Digs).DumpedAt!.Name);
    }

    [Fact]
    public async Task Loading_and_saving_an_aggregate_never_tracks_the_one_it_refers_to()
    {
        await using var database = await StartDatabaseAsync();
        var (_, excavator) = await SeedAsync();
        var dig = NewDig(excavator.Id, 12m);
        await dig.Save(cancellationToken: Token);
        var context = database.Context;

        var loaded = await context.Set<Dig>().FirstAsync(d => d.Id == dig.Id, Token);

        Assert.Equal(excavator.Id, loaded.Excavator.Id);
        Assert.Empty(context.ChangeTracker.Entries<Excavator>());
    }

    [Fact]
    public async Task A_reference_to_a_row_that_does_not_exist_is_refused_by_the_store()
    {
        await using var database = await StartDatabaseAsync();

        var orphan = NewDig(ExcavatorId.New(), 1m);

        await Assert.ThrowsAnyAsync<DbUpdateException>(() => orphan.Save(cancellationToken: Token));
    }

    // The internal factories alone: built, not saved. Their generated entries (which save) take a token, so a test method
    // calling the factories by name reads to xUnit as a call that drops one.
    private static Excavator NewExcavator(string make, Guid? quarry) => Excavator.Made(make, quarry);

    private static Dig NewDig(ExcavatorId excavator, decimal tonnes) => Dig.By(excavator, tonnes);

    private Task<TestDatabase> StartDatabaseAsync() =>
        TestDatabase.Start(o => o.UseSqlite(_sqlite.ConnectionString));

    private static async Task<(Quarry Quarry, Excavator Excavator)> SeedAsync()
    {
        var quarry = Quarry.Named("Gánt");
        await quarry.Save(cancellationToken: Token);
        var excavator = NewExcavator("Liebherr", quarry.Id);
        await excavator.Save(cancellationToken: Token);
        return (quarry, excavator);
    }
}
