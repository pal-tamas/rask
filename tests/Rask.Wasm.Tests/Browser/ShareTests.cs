using Microsoft.JSInterop;
using Rask.Core.Browser;
using Rask.Wasm.Browser;

namespace Rask.Wasm.Tests.Browser;

public class ShareTests
{
    [Fact]
    public async Task Sharing_calls_navigator_share_with_the_data()
    {
        var js = new FakeJsRuntime();
        var share = new Share(js);
        var data = new ShareData { Title = "Rask", Url = "https://example.com" };

        await share.ShareAsync(data);

        Assert.Equal([data], js.ArgsFor("navigator.share"));
    }

    [Fact]
    public async Task Sharing_null_data_throws()
    {
        var share = new Share(new FakeJsRuntime());

        await Assert.ThrowsAsync<ArgumentNullException>(async () => await share.ShareAsync(null!));
    }

    [Fact]
    public async Task Whether_it_can_share_comes_from_the_runtime()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("navigator.canShare", true);
        var share = new Share(js);

        Assert.True(await share.CanShareAsync());
        Assert.Equal(1, js.CallCount("navigator.canShare"));
    }

    [Fact]
    public async Task It_cannot_share_when_unsupported()
    {
        var js = new FakeJsRuntime();
        js.SetException("navigator.canShare", new JSException("navigator.canShare is not a function"));
        var share = new Share(js);

        Assert.False(await share.CanShareAsync());
    }
}
