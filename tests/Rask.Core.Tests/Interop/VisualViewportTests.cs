using Rask.Core.Browser;

namespace Rask.Core.Tests.Interop;

public class VisualViewportTests
{
    [Fact]
    public async Task Asking_whether_the_visual_viewport_is_supported_calls_the_helper()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskApi.visualViewportSupported", true);

        Assert.True(await new VisualViewportReader(js).IsSupportedAsync());
    }

    [Fact]
    public async Task Getting_the_viewport_gives_the_snapshot_from_the_helper()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskApi.visualViewport", new VisualViewport(390, 644, 0, 0, 0, 120, 1.0));

        var v = await new VisualViewportReader(js).GetAsync();

        Assert.NotNull(v);
        Assert.Equal(390, v!.Width);
        Assert.Equal(644, v.Height);
        Assert.Equal(120, v.PageTop);
        Assert.Equal(1.0, v.Scale);
    }

    [Fact]
    public async Task Getting_the_viewport_gives_null_when_unsupported()
    {
        var js = new FakeJsRuntime();

        Assert.Null(await new VisualViewportReader(js).GetAsync());
    }
}
