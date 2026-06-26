using Rask.Core;
using Rask.Wasm.Browser;

namespace Rask.Wasm.Tests.Browser;

public class FullscreenTests
{
    [Fact]
    public async Task IsSupported_CallsHelper()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskFullscreen.isSupported", true);

        Assert.True(await new Fullscreen(js).IsSupportedAsync());
    }

    [Fact]
    public async Task IsActive_CallsHelper()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskFullscreen.isActive", true);

        Assert.True(await new Fullscreen(js).IsActiveAsync());
    }

    [Fact]
    public async Task Request_PassesElementRef()
    {
        var js = new FakeJsRuntime();
        var el = ElementRef.New();

        await new Fullscreen(js).RequestAsync(el);

        Assert.Equal([el], js.ArgsFor("__raskFullscreen.request"));
    }

    [Fact]
    public async Task Request_WithoutElement_PassesNull()
    {
        var js = new FakeJsRuntime();

        await new Fullscreen(js).RequestAsync();

        Assert.Equal(new object?[] { null }, js.ArgsFor("__raskFullscreen.request"));
    }

    [Fact]
    public async Task Exit_CallsHelper()
    {
        var js = new FakeJsRuntime();

        await new Fullscreen(js).ExitAsync();

        Assert.Equal(1, js.CallCount("__raskFullscreen.exit"));
    }
}
