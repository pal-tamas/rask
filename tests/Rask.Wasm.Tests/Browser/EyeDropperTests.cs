using Rask.Wasm.Browser;

namespace Rask.Wasm.Tests.Browser;

public class EyeDropperTests
{
    [Fact]
    public async Task IsSupported_CallsHelper()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskEyeDropper.isSupported", true);

        Assert.True(await new EyeDropper(js).IsSupportedAsync());
    }

    [Fact]
    public async Task Open_ReturnsPickedHex()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskEyeDropper.open", "#3366ff");

        Assert.Equal("#3366ff", await new EyeDropper(js).OpenAsync());
    }

    [Fact]
    public async Task Open_ReturnsNull_WhenCancelled()
    {
        var js = new FakeJsRuntime();

        Assert.Null(await new EyeDropper(js).OpenAsync());
    }
}
