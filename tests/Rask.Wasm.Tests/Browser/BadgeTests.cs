using Rask.Core.Browser;

namespace Rask.Wasm.Tests.Browser;

public class BadgeTests
{
    [Fact]
    public async Task Support_is_asked_of_the_helper()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskBadge.isSupported", true);

        Assert.True(await new Badge(js).IsSupportedAsync());
    }

    [Fact]
    public async Task Setting_a_count_passes_the_number()
    {
        var js = new FakeJsRuntime();

        await new Badge(js).SetAsync(7);

        Assert.Equal([7], js.ArgsFor("__raskBadge.set"));
    }

    [Fact]
    public async Task Setting_without_a_count_passes_null()
    {
        var js = new FakeJsRuntime();

        await new Badge(js).SetAsync();

        Assert.Equal(new object?[] { null }, js.ArgsFor("__raskBadge.set"));
    }

    [Fact]
    public async Task Clearing_calls_the_helper()
    {
        var js = new FakeJsRuntime();

        await new Badge(js).ClearAsync();

        Assert.Equal(1, js.CallCount("__raskBadge.clear"));
    }
}
