using Rask.Core.Browser;

namespace Rask.Core.Tests.Interop;

public class MediaQueryTests
{
    [Fact]
    public async Task Matches_PassesQuery_AndReturnsResult()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskApi.matchMedia", true);

        Assert.True(await new MediaQuery(js).MatchesAsync("(min-width: 768px)"));
        Assert.Equal(["(min-width: 768px)"], js.ArgsFor("__raskApi.matchMedia"));
    }

    [Fact]
    public async Task PrefersDark_UsesColorSchemeQuery()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskApi.matchMedia", true);

        Assert.True(await new MediaQuery(js).PrefersDarkAsync());
        Assert.Equal(["(prefers-color-scheme: dark)"], js.ArgsFor("__raskApi.matchMedia"));
    }

    [Fact]
    public async Task PrefersReducedMotion_UsesReduceQuery()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskApi.matchMedia", true);

        Assert.True(await new MediaQuery(js).PrefersReducedMotionAsync());
        Assert.Equal(["(prefers-reduced-motion: reduce)"], js.ArgsFor("__raskApi.matchMedia"));
    }

    [Fact]
    public async Task Matches_NullQuery_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await new MediaQuery(new FakeJsRuntime()).MatchesAsync(null!));
    }
}
