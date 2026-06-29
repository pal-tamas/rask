using Rask.Core;
using Rask.Wasm.Browser;

namespace Rask.Wasm.Tests.Browser;

public class PictureInPictureTests
{
    [Fact]
    public async Task IsSupported_CallsHelper()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskPip.isSupported", true);

        Assert.True(await new PictureInPicture(js).IsSupportedAsync());
    }

    [Fact]
    public async Task IsActive_CallsHelper()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskPip.isActive", true);

        Assert.True(await new PictureInPicture(js).IsActiveAsync());
    }

    [Fact]
    public async Task Request_PassesElementRef()
    {
        var js = new FakeJsRuntime();
        var el = ElementRef.New();

        await new PictureInPicture(js).RequestAsync(el);

        Assert.Equal([el], js.ArgsFor("__raskPip.request"));
    }

    [Fact]
    public async Task Request_NullElement_Throws()
    {
        var pip = new PictureInPicture(new FakeJsRuntime());
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await pip.RequestAsync(null!));
    }

    [Fact]
    public async Task Exit_CallsHelper()
    {
        var js = new FakeJsRuntime();

        await new PictureInPicture(js).ExitAsync();

        Assert.Equal(1, js.CallCount("__raskPip.exit"));
    }
}
