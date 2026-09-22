using Rask.Core.Browser;

namespace Rask.Wasm.Tests.Browser;

public class WakeLockTests
{
    [Fact]
    public async Task Support_is_asked_of_the_helper()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskWakeLock.isSupported", true);

        Assert.True(await new WakeLock(js).IsSupportedAsync());
    }

    [Fact]
    public async Task A_request_returns_a_sentinel_that_releases_its_id_on_dispose()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskWakeLock.request", 42);

        var sentinel = await new WakeLock(js).RequestAsync();
        await sentinel.DisposeAsync();

        Assert.Equal(1, js.CallCount("__raskWakeLock.request"));
        Assert.Equal([42], js.ArgsFor("__raskWakeLock.release"));
    }

    [Fact]
    public async Task Disposing_the_sentinel_twice_releases_once()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskWakeLock.request", 1);

        var sentinel = await new WakeLock(js).RequestAsync();
        await sentinel.DisposeAsync();
        await sentinel.DisposeAsync();

        Assert.Equal(1, js.CallCount("__raskWakeLock.release"));
    }
}
