using Microsoft.EntityFrameworkCore;

namespace Rask.Outbox.Tests;

/// <summary>
/// An outbox row records the tenant it was enqueued for, and the processor publishes as that tenant.
/// </summary>
/// <remarks>
/// <para>
/// The outbox table is deliberately NOT <c>Tenancy.PerTenant</c>. A partitioned table takes a query filter,
/// and a filter here would hide other tenants' rows from the drain — one processor publishes everybody's
/// work. So the tenant is data on the row rather than a partition of the table.
/// </para>
/// <para>
/// Which is exactly why the processor has to re-enter it: background work carries no principal, so without
/// this a handler that reads a tenant-scoped table throws.
/// </para>
/// </remarks>
[Collection(OutboxDbCollection.Name)]
public sealed class OutboxTenantTests
{
    private readonly Guid _acme = Guid.NewGuid();

    [Fact]
    public void A_message_records_the_tenant_it_was_enqueued_for()
    {
        using (Tenant.Use(_acme))
        {
            Assert.Equal(_acme, OutboxMessage.For("T", "{}", DateTime.UtcNow).TenantId);
        }
    }

    [Fact]
    public void A_message_enqueued_by_the_host_itself_belongs_to_nobody()
    {
        // Not an error: a message raised at startup, or by a console tool, has no tenant. Refusing to write
        // it would be wrong — which is why the row's tenant is read with InFlight rather than Required.
        Assert.Null(OutboxMessage.For("T", "{}", DateTime.UtcNow).TenantId);
    }

    [Fact]
    public void Across_tenants_records_no_tenant()
    {
        // Work that deliberately spans tenants is not being done on behalf of any one of them, so a row it
        // enqueues must not claim otherwise.
        using (Tenant.Across())
        {
            Assert.Null(OutboxMessage.For("T", "{}", DateTime.UtcNow).TenantId);
        }
    }

    [Fact]
    public async Task The_tenant_survives_the_round_trip_to_the_database()
    {
        var path = Path.Combine(Path.GetTempPath(), $"rask-outbox-tenant-{Guid.NewGuid():N}.db");

        try
        {
            var options = new DbContextOptionsBuilder<OutboxDbContext>().UseSqlite($"Data Source={path}").Options;

            await using (var db = new OutboxDbContext(options))
            {
                await db.Database.EnsureCreatedAsync();

                using (Tenant.Use(_acme))
                {
                    db.Add(OutboxMessage.For("T", "{}", DateTime.UtcNow));
                }

                await db.SaveChangesAsync();
            }

            await using (var db = new OutboxDbContext(options))
            {
                // The drain sees it whatever tenant is in flight — no filter on this table, by design.
                var stored = await db.Set<OutboxMessage>().SingleAsync();
                Assert.Equal(_acme, stored.TenantId);
            }
        }
        finally
        {
            File.Delete(path);
        }
    }
}
