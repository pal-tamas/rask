using Rask.Core.Browser;

namespace Rask.Core.Tests.Interop;

public class NavigatorInfoTests
{
    [Fact]
    public async Task OnLine_ReadsNavigatorOnLine_AsProperty()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("navigator.onLine", true);
        var nav = new NavigatorInfo(js);

        var online = await nav.OnLineAsync();

        Assert.True(online);
        // Property read: the identifier carries no args — the client returns the value directly.
        Assert.Empty(js.ArgsFor("navigator.onLine")!);
    }

    [Fact]
    public async Task Language_ReadsNavigatorLanguage()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("navigator.language", "en-US");
        var nav = new NavigatorInfo(js);

        Assert.Equal("en-US", await nav.LanguageAsync());
    }

    [Fact]
    public async Task UserAgent_ReadsNavigatorUserAgent()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("navigator.userAgent", "Mozilla/5.0");
        var nav = new NavigatorInfo(js);

        Assert.Equal("Mozilla/5.0", await nav.UserAgentAsync());
    }
}
