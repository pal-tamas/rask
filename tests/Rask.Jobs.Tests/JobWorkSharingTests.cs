using Rask.Data;

namespace Rask.Background.Tests;

/// <summary>
///     A job its tenant shares (#1368): <c>.Noticed(title).ByTenant()</c> shows it to the tenant's other users who may run its
///     kind — who pass its handler's <c>[Authorize]</c> — and to nobody in another tenant, nobody signed out, and, unmarked, to
///     nobody but its enqueuer.
/// </summary>
[Collection(JobsDbCollection.Name)]
public sealed class JobWorkSharingTests
{
    private static readonly Guid Alice = Guid.Parse("0e4b6c1e-7b52-4f3c-9d2f-1a1a1a1a1a1a");
    private static readonly Guid Bob = Guid.Parse("5d0f2a77-3c11-4b8e-8e0b-2b2b2b2b2b2b");
    private static readonly Guid Carol = Guid.Parse("3f1e2d77-8a11-4c8e-9e0b-5e5e5e5e5e5e");
    private static readonly Guid Acme = Guid.Parse("9a3f6c55-0d4e-4a7b-b1c2-3c3c3c3c3c3c");
    private static readonly Guid Globex = Guid.Parse("c7e1d9a2-6b3f-4e8d-a5f4-4d4d4d4d4d4d");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_colleague_in_the_tenant_sees_a_shared_job_and_it_says_it_is_not_theirs()
    {
        await using var h = Readers.Harness();
        await Enqueue(h, Alice, Acme, new OpenImport("a"), "Partner load", shared: true);

        var bob = await Readers.Board(h, Bob, Acme);
        var alice = await Readers.Board(h, Alice, Acme);

        var seen = Assert.Single(bob);
        Assert.Equal("Partner load", seen.Title);
        Assert.False(seen.Mine);
        Assert.True(Assert.Single(alice).Mine);
    }

    [Fact]
    public async Task An_unmarked_job_stays_its_enqueuers_alone()
    {
        await using var h = Readers.Harness();
        await Enqueue(h, Alice, Acme, new OpenImport("a"), "Alice's own load", shared: false);

        var bob = await Readers.Board(h, Bob, Acme);

        Assert.Empty(bob);
    }

    [Fact]
    public async Task Another_tenant_never_sees_a_shared_job()
    {
        await using var h = Readers.Harness();
        await Enqueue(h, Alice, Acme, new OpenImport("a"), "Partner load", shared: true);

        var otherTenant = await Readers.Board(h, Bob, Globex);
        var noTenant = await Readers.Board(h, Bob, tenant: null);

        Assert.Empty(otherTenant);
        Assert.Empty(noTenant);
    }

    [Fact]
    public async Task A_colleague_the_kinds_authorize_refuses_never_sees_it_and_one_it_admits_does()
    {
        await using var h = Readers.Harness();
        await Enqueue(h, Alice, Acme, new LoaderImport("a"), "Loader's load", shared: true);

        var bob = await Readers.Board(h, Bob, Acme);
        var carol = await Readers.Board(h, Carol, Acme, "loader");
        var alice = await Readers.Board(h, Alice, Acme);

        Assert.Empty(bob);
        Assert.Equal("Loader's load", Assert.Single(carol).Title);
        Assert.Single(alice); // her own, whatever her roles
    }

    [Fact]
    public async Task The_watch_hears_a_shared_job_only_for_a_colleague_its_kind_admits_in_its_tenant()
    {
        await using var h = Readers.Harness(o => o.MaxAttempts = 1);
        var bob = await Readers.Watch(h, Bob, Acme);
        var carol = await Readers.Watch(h, Carol, Acme, "loader");
        var elsewhere = await Readers.Watch(h, Carol, Globex, "loader");
        await using var heard = await HeardWork.Everything(h);
        await Enqueue(h, Alice, Acme, new LoaderImport("a"), "Loader's load", shared: true);
        await Enqueue(h, Alice, Acme, new OpenImport("b"), "Alice's own load", shared: false);

        await h.RunUntilAsync(() => heard.Heard.Count(w => w.HasEnded) == 2);

        Assert.Equal(["Loader's load"], heard.Heard.Where(carol!.Matches).Select(w => w.Title).Distinct());
        Assert.DoesNotContain(heard.Heard, bob!.Matches);
        Assert.DoesNotContain(heard.Heard, elsewhere!.Matches);
    }

    [Fact]
    public async Task Nobody_signed_in_may_open_the_watch()
    {
        await using var h = Readers.Harness();

        var nobody = await Readers.Watch(h, user: null, Acme);

        Assert.Null(nobody);
    }

    [Fact]
    public async Task By_tenant_with_no_tenant_in_flight_throws_naming_the_call()
    {
        await using var h = Readers.Harness();

        InvalidOperationException refused;
        using (Current.UseUser(Alice))
        {
            refused = await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await h.Queue.Enqueue(new OpenImport("a"), Ct).Noticed("Partner load").ByTenant());
        }

        Assert.Contains("Jobs.Enqueue(job).Noticed(…).ByTenant()", refused.Message, StringComparison.Ordinal);
        Assert.Equal(0, await h.CountJobsAsync());
    }

    [Fact]
    public async Task By_tenant_without_a_notice_throws_naming_what_to_word_first()
    {
        await using var h = Readers.Harness();

        InvalidOperationException refused;
        using (Current.UseUser(Alice))
        using (Tenant.Use(Acme))
        {
            refused = await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await h.Queue.Run(new OpenImport("a"), Ct).ByTenant());
        }

        Assert.Contains("Jobs.Run(job).ByTenant()", refused.Message, StringComparison.Ordinal);
        Assert.Contains(".Noticed(", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_shared_job_writes_shared_on_its_row_and_an_unmarked_one_does_not()
    {
        await using var h = Readers.Harness();
        await Enqueue(h, Alice, Acme, new OpenImport("shared"), "Partner load", shared: true);
        await Enqueue(h, Alice, Acme, new OpenImport("own"), "Own load", shared: false);

        var shared = await h.Row(await h.IdOf("shared"));
        var own = await h.Row(await h.IdOf("own"));

        Assert.True(shared!.Shared);
        Assert.False(own!.Shared);
    }

    private static async Task Enqueue(JobsHarness h, Guid user, Guid tenant, IJob job, string title, bool shared)
    {
        using var who = Current.UseUser(user);
        using var where = Tenant.Use(tenant);
        var enqueuing = h.Queue.Enqueue(job, Ct).Noticed(title);
        await (shared ? enqueuing.ByTenant() : enqueuing);
    }
}
