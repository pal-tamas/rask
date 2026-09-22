using Rask.Core.Browser;

namespace Rask.Core.Tests.Interop;

public class MediaQueryTests
{
    [Fact]
    public async Task Matching_passes_the_query_and_gives_the_result()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskApi.matchMedia", true);

        Assert.True(await new MediaQuery(js).MatchesAsync("(min-width: 768px)"));
        Assert.Equal(["(min-width: 768px)"], js.ArgsFor("__raskApi.matchMedia"));
    }

    [Fact]
    public async Task PrefersDark_uses_the_color_scheme_query()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskApi.matchMedia", true);

        Assert.True(await new MediaQuery(js).PrefersDarkAsync());
        Assert.Equal(["(prefers-color-scheme: dark)"], js.ArgsFor("__raskApi.matchMedia"));
    }

    [Fact]
    public async Task PrefersReducedMotion_uses_the_reduce_query()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskApi.matchMedia", true);

        Assert.True(await new MediaQuery(js).PrefersReducedMotionAsync());
        Assert.Equal(["(prefers-reduced-motion: reduce)"], js.ArgsFor("__raskApi.matchMedia"));
    }

    [Fact]
    public async Task Matching_a_null_query_throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await new MediaQuery(new FakeJsRuntime()).MatchesAsync(null!));
    }
}
