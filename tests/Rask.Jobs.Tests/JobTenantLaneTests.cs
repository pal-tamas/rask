using Microsoft.EntityFrameworkCore;
using Rask.Data;

namespace Rask.Background.Tests;

/// <summary>
/// F-27: <c>.InTenantLane("loads")</c> runs one tenant's work one at a time, beside every other tenant's — the lane named
/// after the tenant in flight, so no app writes its own per-tenant turn.
/// </summary>
[Collection(JobsDbCollection.Name)]
public sealed class JobTenantLaneTests
{
    private static readonly Guid Acme = Guid.Parse("0f8b6f1e-1a59-4d0e-9a3a-4c1f7b2f0a01");
    private static readonly Guid Globex = Guid.Parse("7c3d2b9a-5e41-4f6b-8d2c-9a0e1b3c4d02");

    [Fact]
    public async Task One_tenants_jobs_wait_their_turn_while_another_tenants_run_beside_them()
    {
        await using var h = new JobsHarness();
        using (Tenant.Use(Acme))
        {
            await h.Queue.Enqueue(new RecordJob("acme-1"), TestContext.Current.CancellationToken).InTenantLane("loads");
            await h.Queue.Enqueue(new RecordJob("acme-2"), TestContext.Current.CancellationToken).InTenantLane("loads");
        }

        using (Tenant.Use(Globex))
        {
            await h.Queue.Enqueue(new RecordJob("globex-1"), TestContext.Current.CancellationToken).InTenantLane("loads");
        }

        await using var db = h.NewContext();
        var claimed = await h.Jobs.ClaimAsync(db, h.Clock.GetUtcNow().UtcDateTime, TestContext.Current.CancellationToken);

        Assert.Equal(2, claimed.Count);
        Assert.Equal([$"loads/{Acme:N}", $"loads/{Globex:N}"], claimed.Select(j => j.Lane).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void The_tenant_lane_is_named_after_the_tenant_in_flight()
    {
        using var where = Tenant.Use(Acme);

        var loads = Jobs.TenantLane("loads");
        var only = Jobs.TenantLane();

        Assert.Equal($"loads/{Acme:N}", loads);
        Assert.Equal($"tenant/{Acme:N}", only);
    }

    [Fact]
    public async Task A_batch_takes_the_tenant_lane_too()
    {
        await using var h = new JobsHarness();
        using var where = Tenant.Use(Acme);

        await new Enqueuing<RecordJob>(h.Queue, [new RecordJob("a"), new RecordJob("b")], TestContext.Current.CancellationToken).InTenantLane("loads");

        await using var db = h.NewContext();
        var rows = await db.Set<Job>().ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, rows.Count);
        Assert.All(rows, job => Assert.Equal($"loads/{Acme:N}", job.Lane));
    }

    [Fact]
    public async Task With_no_tenant_in_flight_the_step_says_so_and_names_the_call()
    {
        await using var h = new JobsHarness();

        var refused = Record.Exception(() => h.Queue.Enqueue(new RecordJob("x"), TestContext.Current.CancellationToken).InTenantLane("loads"));

        var error = Assert.IsType<InvalidOperationException>(refused);
        Assert.Contains("Jobs.Enqueue(job).InTenantLane()", error.Message, StringComparison.Ordinal);
        Assert.Equal(0, await h.CountJobsAsync());
    }
}
