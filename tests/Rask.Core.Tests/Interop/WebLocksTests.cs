using Rask.Core.Browser;

namespace Rask.Core.Tests.Interop;

public class WebLocksTests
{
    [Fact]
    public async Task IsSupported_CallsHelper() =>
        Assert.Equal(
            "__raskLocks.isSupported",
            await SupportCall());

    private static async Task<string> SupportCall()
    {
        var js = new FakeJsRuntime();
        await new WebLocks(js).IsSupportedAsync();
        return js.Calls.Single().Identifier;
    }

    [Fact]
    public async Task Request_AcquiresRunsWork_ThenReleasesInOrder()
    {
        var js = new FakeJsRuntime();
        var workRan = false;
        var releasedBeforeWork = true;

        await new WebLocks(js).RequestAsync("token-refresh", () =>
        {
            workRan = true;
            // request must have happened, release must NOT have happened yet.
            releasedBeforeWork = js.CallCount("__raskLocks.release") != 0;
            return Task.CompletedTask;
        });

        Assert.True(workRan);
        Assert.False(releasedBeforeWork);

        var reqArgs = js.ArgsFor("__raskLocks.request");
        var id = Assert.IsType<int>(reqArgs![0]);
        Assert.Equal("token-refresh", reqArgs[1]);
        Assert.Equal("exclusive", reqArgs[2]);
        Assert.Equal(false, reqArgs[3]); // not ifAvailable
        Assert.Equal([id], js.ArgsFor("__raskLocks.release"));

        // Overall order: request then release.
        Assert.Equal(
            ["__raskLocks.request", "__raskLocks.release"],
            js.Calls.Select(c => c.Identifier));
    }

    [Fact]
    public async Task Request_SharedMode_PassesSharedString()
    {
        var js = new FakeJsRuntime();
        await new WebLocks(js).RequestAsync("feed", () => Task.CompletedTask, LockMode.Shared);
        Assert.Equal("shared", js.ArgsFor("__raskLocks.request")![2]);
    }

    [Fact]
    public async Task Request_ReleasesEvenWhenWorkThrows()
    {
        var js = new FakeJsRuntime();

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await new WebLocks(js).RequestAsync("x", () => throw new InvalidOperationException("boom")));

        var id = (int)js.ArgsFor("__raskLocks.request")![0]!;
        Assert.Equal([id], js.ArgsFor("__raskLocks.release"));
    }

    [Fact]
    public async Task TryRequest_WhenGranted_RunsWorkReleases_ReturnsTrue()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskLocks.request", true);
        var workRan = false;

        var ok = await new WebLocks(js).TryRequestAsync("leader", () =>
        {
            workRan = true;
            return Task.CompletedTask;
        });

        Assert.True(ok);
        Assert.True(workRan);
        Assert.Equal(true, js.ArgsFor("__raskLocks.request")![3]); // ifAvailable
        Assert.Equal(1, js.CallCount("__raskLocks.release"));
    }

    [Fact]
    public async Task TryRequest_WhenNotGranted_SkipsWork_NoRelease_ReturnsFalse()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskLocks.request", false); // lock already held
        var workRan = false;

        var ok = await new WebLocks(js).TryRequestAsync("leader", () =>
        {
            workRan = true;
            return Task.CompletedTask;
        });

        Assert.False(ok);
        Assert.False(workRan);
        Assert.Equal(0, js.CallCount("__raskLocks.release"));
    }

    [Fact]
    public async Task Query_MapsHeldAndPendingLocks()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskLocks.query", new[]
        {
            new LockInfo("a", "exclusive", "c1", true),
            new LockInfo("b", "shared", null, false),
        });

        var locks = await new WebLocks(js).QueryAsync();

        Assert.Collection(locks,
            l => Assert.True(l is { Name: "a", Mode: "exclusive", ClientId: "c1", Held: true }),
            l => Assert.True(l is { Name: "b", Mode: "shared", ClientId: null, Held: false }));
    }

    [Fact]
    public async Task Query_NullResult_IsEmpty() =>
        Assert.Empty(await new WebLocks(new FakeJsRuntime()).QueryAsync());

    [Fact]
    public async Task NullArgs_Throw()
    {
        var svc = new WebLocks(new FakeJsRuntime());
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await svc.RequestAsync(null!, () => Task.CompletedTask));
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await svc.RequestAsync("n", null!));
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await svc.TryRequestAsync(null!, () => Task.CompletedTask));
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await svc.TryRequestAsync("n", null!));
    }
}
