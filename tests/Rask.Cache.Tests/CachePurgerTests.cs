using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;

namespace Rask.Cache.Tests;

[Collection(CacheDbCollection.Name)]
public sealed class CachePurgerTests
{
    private static byte[] Bytes(string s) => Encoding.UTF8.GetBytes(s);

    [Fact]
    public async Task Purger_deletes_expired_entries_and_keeps_fresh_ones()
    {
        await using var harness = new CacheHarness();
        await harness.Distributed.SetAsync("keep", Bytes("v"), new DistributedCacheEntryOptions());
        await harness.Distributed.SetAsync("drop", Bytes("v"),
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5) });

        harness.Clock.Advance(TimeSpan.FromMinutes(6));

        await harness.Purger.StartAsync(CancellationToken.None);
        try
        {
            await harness.WaitUntilAsync(async () => await harness.CountEntriesAsync() == 1);
        }
        finally
        {
            await harness.Purger.StopAsync(CancellationToken.None);
        }

        await using var db = harness.NewContext();
        var remaining = await db.Set<CacheEntry>().Select(e => e.Key).ToListAsync();
        Assert.Equal(["keep"], remaining);
    }
}
