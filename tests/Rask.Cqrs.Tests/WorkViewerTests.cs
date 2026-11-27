using Rask.Background;

namespace Rask.Cqrs.Tests;

// Who sees which work (#1368): their own in their tenant, and their tenant's shared work of a kind that admits them — never
// another tenant's, never shared work of a kind that refuses them, never a shared job nobody enqueued.
public sealed class WorkViewerTests
{
    private static readonly Guid Alice = Guid.Parse("8f0f6a51-5a3c-4b0e-9b8e-6f6b8c1d2e3f");
    private static readonly Guid Bob = Guid.Parse("2a0f6a51-5a3c-4b0e-9b8e-6f6b8c1d2e3f");
    private static readonly Guid Acme = Guid.Parse("1b2c3d4e-0000-4000-8000-00000000a0a0");
    private static readonly Guid Globex = Guid.Parse("1b2c3d4e-0000-4000-8000-00000000b0b0");
    private static readonly DateTime At = new(2026, 11, 18, 9, 30, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData("alice's own, unshared", true)]
    [InlineData("alice's own, in another tenant", false)]
    [InlineData("alice's shared, of a kind bob may run", true)]
    [InlineData("alice's shared, of a kind refused to bob", false)]
    [InlineData("alice's unshared", false)]
    [InlineData("alice's shared, in another tenant", false)]
    [InlineData("shared by nobody", false)]
    public void Bob_sees_his_own_and_his_tenants_shared_work_he_may_run_and_nothing_else(string which, bool seen)
    {
        var bob = new WorkViewer(Bob, Acme, new HashSet<string>(StringComparer.Ordinal) { "Loads.Restricted" });
        var work = which switch
        {
            "alice's own, unshared" => Work(Bob, Acme, shared: false),
            "alice's own, in another tenant" => Work(Bob, Globex, shared: false),
            "alice's shared, of a kind bob may run" => Work(Alice, Acme, shared: true),
            "alice's shared, of a kind refused to bob" => Work(Alice, Acme, shared: true, kind: "Loads.Restricted"),
            "alice's unshared" => Work(Alice, Acme, shared: false),
            "alice's shared, in another tenant" => Work(Alice, Globex, shared: true),
            _ => Work(owner: null, Acme, shared: true),
        };

        var may = bob.May(work);

        Assert.Equal(seen, may);
    }

    [Fact]
    public void A_reader_with_no_tenant_sees_only_their_own_tenantless_work()
    {
        var bob = new WorkViewer(Bob, Tenant: null, new HashSet<string>());

        var own = bob.May(Work(Bob, tenant: null, shared: false));
        var shared = bob.May(Work(Alice, tenant: null, shared: true));

        Assert.True(own);
        Assert.False(shared);
    }

    [Fact]
    public void An_unadmitted_watch_hears_nothing_and_an_admitted_one_what_its_reader_may_see()
    {
        var watch = new WatchOwnWork(Lane: null);
        var shared = Work(Alice, Acme, shared: true);

        var before = watch.Matches(shared);
        watch.Admit(new WorkViewer(Bob, Acme, new HashSet<string>()));
        var after = watch.Matches(shared);

        Assert.False(before);
        Assert.True(after);
    }

    private static WorkReported Work(Guid? owner, Guid? tenant, bool shared, string kind = "Loads.Open") =>
        new(Guid.CreateVersion7(), "Load", null, null, null, WorkState.Running, At) { Owner = owner, Tenant = tenant, Shared = shared, Kind = kind };
}
