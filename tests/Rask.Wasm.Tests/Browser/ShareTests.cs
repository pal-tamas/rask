using Microsoft.JSInterop;
using Rask.Wasm.Browser;

namespace Rask.Wasm.Tests.Browser;

public class ShareTests
{
    [Fact]
    public async Task Share_SendsNavigatorShare_WithData()
    {
        var js = new FakeJsRuntime();
        var share = new Share(js);
        var data = new ShareData { Title = "Rask", Url = "https://example.com" };

        await share.ShareAsync(data);

        Assert.Equal([data], js.ArgsFor("navigator.share"));
    }

    [Fact]
    public async Task Share_NullData_Throws()
    {
        var share = new Share(new FakeJsRuntime());

        await Assert.ThrowsAsync<ArgumentNullException>(async () => await share.ShareAsync(null!));
    }

    [Fact]
    public async Task CanShare_ReturnsRuntimeResult()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("navigator.canShare", true);
        var share = new Share(js);

        Assert.True(await share.CanShareAsync());
        Assert.Equal(1, js.CallCount("navigator.canShare"));
    }

    [Fact]
    public async Task CanShare_ReturnsFalse_WhenUnsupported()
    {
        var js = new FakeJsRuntime();
        js.SetException("navigator.canShare", new JSException("navigator.canShare is not a function"));
        var share = new Share(js);

        Assert.False(await share.CanShareAsync());
    }
}
