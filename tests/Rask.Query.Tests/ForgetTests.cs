namespace Rask.Querying.Tests;

/// <summary>
///     A session that now acts for another user, or in another tenant, forgets what it read for the one before:
///     nothing of it is served again, not even while its replacement loads.
/// </summary>
public class ForgetTests
{
    [Fact]
    public async Task What_was_read_is_not_served_after_it_is_forgotten()
    {
        var dispatcher = new CountingDispatcher();
        var client = new SessionQueryClient(dispatcher, new TestClock(DateTimeOffset.UnixEpoch));
        using var query = client.Query(new GetOrders(1));
        await SettleAsync(query);
        dispatcher.Block();

        client.Forget();
        var served = query.Data;

        Assert.Null(served);
        Assert.True(query.IsLoading);
    }

    [Fact]
    public async Task What_renders_a_forgotten_read_reads_it_again()
    {
        var dispatcher = new CountingDispatcher();
        var client = new SessionQueryClient(dispatcher, new TestClock(DateTimeOffset.UnixEpoch));
        using var query = client.Query(new GetOrders(1));
        await SettleAsync(query);
        dispatcher.Result = "second";

        client.Forget();
        await SettleAsync(query);

        Assert.Equal("second", query.Data);
        Assert.Equal(2, dispatcher.QueryCount);
    }

    // The fetch began for the reader before. Its answer is theirs, whenever it arrives.
    [Fact]
    public async Task A_read_still_on_its_way_when_it_was_forgotten_does_not_land()
    {
        var dispatcher = new CountingDispatcher();
        var client = new SessionQueryClient(dispatcher, new TestClock(DateTimeOffset.UnixEpoch));
        dispatcher.Block();
        using var query = client.Query(new GetOrders(1));
        _ = query.Data;
        var before = query.Entry.InFlight!;

        client.Forget();
        dispatcher.Result = "second";
        dispatcher.Release();
        await before;
        await SettleAsync(query);

        Assert.Equal("second", query.Data);
    }

    private static async Task SettleAsync<T>(Query<T> query)
    {
        _ = query.Data;
        if (query.Entry.InFlight is { } running)
        {
            await running;
        }
    }
}
