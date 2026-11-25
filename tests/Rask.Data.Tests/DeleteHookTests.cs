using Microsoft.EntityFrameworkCore;

namespace Rask.Data.Tests;

public sealed record HaulerRemoved(Guid Id, IReadOnlyList<string> Chips) : IEvent;

/// <summary>A hauler whose delete tells who held its chips: its <c>OnDelete()</c> raises the event.</summary>
public sealed partial class Hauler : Aggregate<Guid, SoftDelete>
{
    private Hauler() { } // EF materialization

    public string Chip { get; private set; } = "";

    internal static Hauler Fitted(string chip) => new() { Id = Guid.CreateVersion7(), Chip = chip };

    private void OnDelete() => Raise(new HaulerRemoved(Id, [Chip]));
}

/// <summary>
///     An aggregate's <c>private void OnDelete()</c> runs in every save that deletes it, so what it raises is stored and
///     published with the delete — the generated <c>X.Delete</c> included — and never on a rollback.
/// </summary>
[Collection(DataDbCollection.Name)]
public sealed class DeleteHookTests : IDisposable
{
    private readonly InMemorySqlite _sqlite = new();

    public void Dispose() => _sqlite.Dispose();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_generated_delete_publishes_what_OnDelete_raises()
    {
        await using var database = await TestDatabase.Start(o => o.UseSqlite(_sqlite.ConnectionString), cancellationToken: Token);
        var id = await Hauler.Fitted("A-17", cancellationToken: Token);
        using var events = Events.Fake();

        await Hauler.Delete(id, cancellationToken: Token);

        var removed = events.Published<HaulerRemoved>().Only();
        Assert.Equal(id, removed.Id);
        Assert.Equal(["A-17"], removed.Chips);
    }

    [Fact]
    public async Task A_hand_removal_and_save_raises_it_once()
    {
        await using var database = await TestDatabase.Start(o => o.UseSqlite(_sqlite.ConnectionString), cancellationToken: Token);
        var id = await Hauler.Fitted("A-17", cancellationToken: Token);
        using var events = Events.Fake();

        var tracked = await database.Context.Set<Hauler>().SingleAsync(h => h.Id == id, Token);
        database.Context.Remove(tracked);
        await database.Context.SaveChangesAsync(Token);

        events.Published<HaulerRemoved>().Once();
    }

    [Fact]
    public async Task A_delete_rolled_back_publishes_nothing()
    {
        await using var database = await TestDatabase.Start(o => o.UseSqlite(_sqlite.ConnectionString), cancellationToken: Token);
        var id = await Hauler.Fitted("A-17", cancellationToken: Token);
        using var events = Events.Fake();

        await Assert.ThrowsAsync<InvalidOperationException>(() => Db.Transaction(async () =>
        {
            await Hauler.Delete(id, cancellationToken: Token);
            throw new InvalidOperationException("changed my mind");
        }, cancellationToken: Token));

        events.Published<HaulerRemoved>().None();
    }
}
