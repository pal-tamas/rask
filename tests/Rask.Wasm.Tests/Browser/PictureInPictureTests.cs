using Rask.Core;
using Rask.Wasm.Browser;

namespace Rask.Wasm.Tests.Browser;

public class PictureInPictureTests
{
    [Fact]
    public async Task Support_is_asked_of_the_helper()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskPip.isSupported", true);

        Assert.True(await new PictureInPicture(js).IsSupportedAsync());
    }

    [Fact]
    public async Task Whether_it_is_active_is_asked_of_the_helper()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskPip.isActive", true);

        Assert.True(await new PictureInPicture(js).IsActiveAsync());
    }

    [Fact]
    public async Task A_request_passes_the_element_ref()
    {
        var js = new FakeJsRuntime();
        var el = ElementRef.New();

        await new PictureInPicture(js).RequestAsync(el);

        Assert.Equal([el], js.ArgsFor("__raskPip.request"));
    }

    [Fact]
    public async Task A_request_for_a_null_element_throws()
    {
        var pip = new PictureInPicture(new FakeJsRuntime());

        await Assert.ThrowsAsync<ArgumentNullException>(async () => await pip.RequestAsync(null!));
    }

    [Fact]
    public async Task Exiting_calls_the_helper()
    {
        var js = new FakeJsRuntime();

        await new PictureInPicture(js).ExitAsync();

        Assert.Equal(1, js.CallCount("__raskPip.exit"));
    }
}
