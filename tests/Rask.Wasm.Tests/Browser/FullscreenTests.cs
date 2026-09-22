using Rask.Core;
using Rask.Wasm.Browser;

namespace Rask.Wasm.Tests.Browser;

public class FullscreenTests
{
    [Fact]
    public async Task Support_is_asked_of_the_helper()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskFullscreen.isSupported", true);

        Assert.True(await new Fullscreen(js).IsSupportedAsync());
    }

    [Fact]
    public async Task Whether_it_is_active_is_asked_of_the_helper()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskFullscreen.isActive", true);

        Assert.True(await new Fullscreen(js).IsActiveAsync());
    }

    [Fact]
    public async Task A_request_passes_the_element_ref()
    {
        var js = new FakeJsRuntime();
        var el = ElementRef.New();

        await new Fullscreen(js).RequestAsync(el);

        Assert.Equal([el], js.ArgsFor("__raskFullscreen.request"));
    }

    [Fact]
    public async Task A_request_without_an_element_passes_null()
    {
        var js = new FakeJsRuntime();

        await new Fullscreen(js).RequestAsync();

        Assert.Equal(new object?[] { null }, js.ArgsFor("__raskFullscreen.request"));
    }

    [Fact]
    public async Task Exiting_calls_the_helper()
    {
        var js = new FakeJsRuntime();

        await new Fullscreen(js).ExitAsync();

        Assert.Equal(1, js.CallCount("__raskFullscreen.exit"));
    }
}
