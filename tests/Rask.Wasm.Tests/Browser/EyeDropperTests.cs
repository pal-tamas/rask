using Rask.Wasm.Browser;

namespace Rask.Wasm.Tests.Browser;

public class EyeDropperTests
{
    [Fact]
    public async Task Support_is_asked_of_the_helper()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskEyeDropper.isSupported", true);

        Assert.True(await new EyeDropper(js).IsSupportedAsync());
    }

    [Fact]
    public async Task Opening_returns_the_picked_hex()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskEyeDropper.open", "#3366ff");

        Assert.Equal("#3366ff", await new EyeDropper(js).OpenAsync());
    }

    [Fact]
    public async Task Opening_returns_null_when_cancelled()
    {
        var js = new FakeJsRuntime();

        Assert.Null(await new EyeDropper(js).OpenAsync());
    }
}
