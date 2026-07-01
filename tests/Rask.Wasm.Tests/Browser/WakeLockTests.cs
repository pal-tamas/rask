using Rask.Core.Browser;

namespace Rask.Wasm.Tests.Browser;

public class WakeLockTests
{
    [Fact]
    public async Task IsSupported_CallsHelper()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskWakeLock.isSupported", true);

        Assert.True(await new WakeLock(js).IsSupportedAsync());
    }

    [Fact]
    public async Task Request_ReturnsSentinel_ThatReleasesIdOnDispose()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskWakeLock.request", 42);

        var sentinel = await new WakeLock(js).RequestAsync();
        await sentinel.DisposeAsync();

        Assert.Equal(1, js.CallCount("__raskWakeLock.request"));
        Assert.Equal([42], js.ArgsFor("__raskWakeLock.release"));
    }

    [Fact]
    public async Task Dispose_IsIdempotent()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskWakeLock.request", 1);

        var sentinel = await new WakeLock(js).RequestAsync();
        await sentinel.DisposeAsync();
        await sentinel.DisposeAsync();

        Assert.Equal(1, js.CallCount("__raskWakeLock.release"));
    }
}
