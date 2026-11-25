using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Rask.Tests.Shared;

namespace Rask.Outbox.Tests;

// A declared projection's event is stored in the outbox in the save that changed its rows — committed with them, or not
// at all.
public sealed class ProjectionOutboxTests : IDisposable
{
    private readonly ShippingLog _log = new();
    private readonly InMemorySqlite _db = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task A_projected_event_with_a_durable_handler_is_stored_in_the_save_that_changed_its_rows()
    {
        await using var database = await Start();

        database.Context.AddRange(Parcel.Weighed(3), Parcel.Weighed(5));
        await database.Context.SaveChangesAsync(Ct);

        var stored = await database.Context.Set<OutboxMessage>().Select(m => m.Payload).ToListAsync(Ct);
        Assert.Contains("[3,5]", Assert.Single(stored), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_rolled_back_save_leaves_no_outbox_row_for_its_projected_event()
    {
        await using var database = await Start();

        await using (var transaction = await database.Context.Database.BeginTransactionAsync(Ct))
        {
            database.Context.Add(Parcel.Weighed(3));
            await database.Context.SaveChangesAsync(Ct);
            await transaction.RollbackAsync(Ct);
        }

        Assert.Equal(0, await database.Context.Set<OutboxMessage>().CountAsync(Ct));
    }

    private Task<TestDatabase<ShopWithOutboxContext>> Start() =>
        TestDatabase.Start<ShopWithOutboxContext>(
            o => o.UseSqlite(_db.ConnectionString),
            services: s => s.AddSingleton(_log).AddSingleton(new Recorder()),
            cancellationToken: Ct);
}
