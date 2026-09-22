using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Rask.Data;

namespace Rask.Cache.Tests;

/// <summary>
/// One cache, isolated per tenant by the KEY rather than by a query filter.
/// </summary>
/// <remarks>
/// <para>
/// A filter is the wrong tool here twice over. The purger has to sweep every tenant's expired rows, and a
/// filter would hide them from it. And ASP.NET's own session and output caching write through
/// <c>IDistributedCache</c> on requests with no signed-in user at all — a partitioned table is stamped from
/// the ambient tenant and refused without one, so an anonymous page with output caching would throw.
/// </para>
/// <para>
/// Scoping the key makes the isolation exact rather than enforced: a tenant cannot form another tenant's key,
/// so there is nothing to get wrong.
/// </para>
/// </remarks>
[Collection(CacheDbCollection.Name)]
public sealed class CacheTenantTests
{
    private readonly Guid _acme = Guid.NewGuid();
    private readonly Guid _globex = Guid.NewGuid();

    private static byte[] Bytes(string s) => Encoding.UTF8.GetBytes(s);

    private static string Str(byte[] b) => Encoding.UTF8.GetString(b);

    [Fact]
    public async Task One_key_holds_a_different_value_per_tenant()
    {
        await using var harness = new CacheHarness();

        using (Tenant.Use(_acme))
        {
            await harness.Distributed.SetAsync("greeting", Bytes("acme"), new DistributedCacheEntryOptions());
        }

        using (Tenant.Use(_globex))
        {
            await harness.Distributed.SetAsync("greeting", Bytes("globex"), new DistributedCacheEntryOptions());
        }

        using (Tenant.Use(_acme))
        {
            Assert.Equal("acme", Str((await harness.Distributed.GetAsync("greeting"))!));
        }

        using (Tenant.Use(_globex))
        {
            Assert.Equal("globex", Str((await harness.Distributed.GetAsync("greeting"))!));
        }
    }

    [Fact]
    public async Task A_tenant_cannot_read_anothers_entry()
    {
        await using var harness = new CacheHarness();

        using (Tenant.Use(_acme))
        {
            await harness.Distributed.SetAsync("secret", Bytes("acme"), new DistributedCacheEntryOptions());
        }

        using (Tenant.Use(_globex))
        {
            Assert.Null(await harness.Distributed.GetAsync("secret"));
        }
    }

    [Fact]
    public async Task Removing_one_tenants_entry_leaves_the_others()
    {
        await using var harness = new CacheHarness();

        using (Tenant.Use(_acme))
        {
            await harness.Distributed.SetAsync("k", Bytes("acme"), new DistributedCacheEntryOptions());
        }

        using (Tenant.Use(_globex))
        {
            await harness.Distributed.SetAsync("k", Bytes("globex"), new DistributedCacheEntryOptions());
            await harness.Distributed.RemoveAsync("k");
        }

        using (Tenant.Use(_acme))
        {
            Assert.Equal("acme", Str((await harness.Distributed.GetAsync("k"))!));
        }
    }

    [Fact]
    public async Task With_no_tenant_the_key_is_untouched()
    {
        await using var harness = new CacheHarness();

        // The case that rules out a query filter: ASP.NET session and output caching run on anonymous
        // requests, where there is no tenant at all. That has to keep working exactly as before.
        await harness.Distributed.SetAsync("anonymous", Bytes("v"), new DistributedCacheEntryOptions());

        Assert.Equal("v", Str((await harness.Distributed.GetAsync("anonymous"))!));
        await using var db = harness.NewContext();
        Assert.True(await db.Set<CacheEntry>().AnyAsync(e => e.Key == "anonymous"));
    }

    [Fact]
    public async Task The_purger_still_sees_every_tenants_rows()
    {
        await using var harness = new CacheHarness();

        using (Tenant.Use(_acme))
        {
            await harness.Distributed.SetAsync("a", Bytes("1"), new DistributedCacheEntryOptions());
        }

        using (Tenant.Use(_globex))
        {
            await harness.Distributed.SetAsync("b", Bytes("2"), new DistributedCacheEntryOptions());
        }

        // No filter on the table, so a sweep reaches both — which is the whole reason the isolation is in
        // the key rather than in a query filter.
        await using var db = harness.NewContext();
        Assert.Equal(2, await db.Set<CacheEntry>().CountAsync());
    }
}
