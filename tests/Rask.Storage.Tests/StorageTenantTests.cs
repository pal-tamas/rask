using Microsoft.EntityFrameworkCore;
using Rask.Data;

namespace Rask.Storage.Tests;

/// <summary>
/// A file belongs to the tenant that saved it, and cannot be reached from another — even holding its id.
/// </summary>
/// <remarks>
/// The table carries the tenant rather than being partitioned by one. A partitioned table takes a query
/// filter, and a filter would hide other tenants' rows from the orphan sweep, which has to see every file to
/// decide what is unreferenced. So the scoping is at the two places a file is reached by id.
/// </remarks>
[Collection(StorageDbCollection.Name)]
public sealed class StorageTenantTests
{
    private readonly Guid _acme = Guid.NewGuid();
    private readonly Guid _globex = Guid.NewGuid();

    [Fact]
    public async Task A_file_records_the_tenant_that_saved_it()
    {
        await using var harness = new StorageHarness();

        using (Tenant.Use(_acme))
        {
            using var content = new MemoryStream(Samples.Text);
            var file = await harness.Files.SaveAsync(content, "data.csv");

            Assert.Equal(_acme, file.TenantId);
        }
    }

    [Fact]
    public async Task A_file_saved_by_the_host_itself_belongs_to_nobody()
    {
        await using var harness = new StorageHarness();
        using var content = new MemoryStream(Samples.Text);

        // Not an error: a file written at startup, or by a console tool, has no tenant.
        var file = await harness.Files.SaveAsync(content, "data.csv");

        Assert.Null(file.TenantId);
    }

    [Fact]
    public async Task Another_tenant_cannot_find_it_even_holding_the_id()
    {
        await using var harness = new StorageHarness();
        Guid id;

        using (Tenant.Use(_acme))
        {
            using var content = new MemoryStream(Samples.Text);
            id = (await harness.Files.SaveAsync(content, "data.csv")).Id;
        }

        using (Tenant.Use(_globex))
        {
            Assert.Null(await harness.Files.FindAsync(id));
            Assert.Null(await harness.Files.OpenReadAsync(id));
        }

        using (Tenant.Use(_acme))
        {
            Assert.NotNull(await harness.Files.FindAsync(id));
        }
    }

    [Fact]
    public async Task Another_tenant_cannot_delete_it()
    {
        await using var harness = new StorageHarness();
        Guid id;

        using (Tenant.Use(_acme))
        {
            using var content = new MemoryStream(Samples.Text);
            id = (await harness.Files.SaveAsync(content, "data.csv")).Id;
        }

        using (Tenant.Use(_globex))
        {
            Assert.False(await harness.Files.DeleteAsync(id));
        }

        using (Tenant.Use(_acme))
        {
            Assert.NotNull(await harness.Files.FindAsync(id));
        }
    }

    [Fact]
    public async Task The_orphan_sweep_still_sees_every_tenants_rows()
    {
        await using var harness = new StorageHarness();

        using (Tenant.Use(_acme))
        {
            using var a = new MemoryStream(Samples.Text);
            await harness.Files.SaveAsync(a, "a.csv");
        }

        using (Tenant.Use(_globex))
        {
            using var b = new MemoryStream(Samples.Text);
            await harness.Files.SaveAsync(b, "b.csv");
        }

        // No filter on the table, so the sweep reaches both — which is why the scoping is in the two
        // by-id queries rather than in a query filter.
        await using var db = harness.NewContext();
        Assert.Equal(2, await db.Set<StoredFile>().CountAsync());
    }
}
