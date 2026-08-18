using Rask.Core.Browser;

namespace Rask.Core.Tests.Interop;

public class ViewTransitionsTests
{
    [Fact]
    public async Task IsSupported_CallsHelper()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskVt.supported", true);

        Assert.True(await new ViewTransitions(js).IsSupportedAsync());
    }

    [Fact]
    public async Task IsSupported_IsFalse_OnABrowserWithoutTheApi()
    {
        // No response registered: the helper reports false rather than throwing, so enabling on Firefox
        // or an older Safari is inert instead of an error.
        var js = new FakeJsRuntime();

        Assert.False(await new ViewTransitions(js).IsSupportedAsync());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SetEnabled_PassesTheFlagAndReturnsWhatTookEffect(bool enabled)
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskVt.set", enabled);

        Assert.Equal(enabled, await new ViewTransitions(js).SetEnabledAsync(enabled));

        var call = Assert.Single(js.Calls, c => c.Identifier == "__raskVt.set");
        Assert.Equal(enabled, call.Args![0]);
    }

    [Fact]
    public async Task IsActive_IsSeparateFromIsEnabled()
    {
        // The distinction the API exists to expose: a settings toggle can be ON while nothing animates,
        // because the browser lacks the API or the reader asked for reduced motion. A UI that conflates
        // the two tells the user their preference was ignored.
        var js = new FakeJsRuntime();
        js.SetResponse("__raskVt.set", true);
        js.SetResponse("__raskVt.active", false);

        var vt = new ViewTransitions(js);

        Assert.True(await vt.SetEnabledAsync(true));
        Assert.False(await vt.IsActiveAsync());
    }
}
