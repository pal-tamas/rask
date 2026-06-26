using Rask.Core.Browser;

namespace Rask.Core.Tests.Interop;

public class ScreenInfoTests
{
    [Fact]
    public async Task Get_ReturnsSnapshot_FromHelper()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskApi.screen", new ScreenInfo(2560, 1440, 2560, 1400, 24, 2.0));

        var info = await new ScreenInfoReader(js).GetAsync();

        Assert.Equal(2560, info.Width);
        Assert.Equal(1440, info.Height);
        Assert.Equal(2560, info.AvailWidth);
        Assert.Equal(1400, info.AvailHeight);
        Assert.Equal(24, info.ColorDepth);
        Assert.Equal(2.0, info.PixelRatio);
    }

    [Fact]
    public async Task Get_CallsHelper_WithNoArgs()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskApi.screen", new ScreenInfo(1920, 1080, 1920, 1040, 24, 1.0));

        await new ScreenInfoReader(js).GetAsync();

        Assert.Empty(js.ArgsFor("__raskApi.screen")!);
    }
}
