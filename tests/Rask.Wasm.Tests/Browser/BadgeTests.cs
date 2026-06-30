using Rask.Core.Browser;

namespace Rask.Wasm.Tests.Browser;

public class BadgeTests
{
    [Fact]
    public async Task IsSupported_CallsHelper()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskBadge.isSupported", true);

        Assert.True(await new Badge(js).IsSupportedAsync());
    }

    [Fact]
    public async Task Set_WithCount_PassesNumber()
    {
        var js = new FakeJsRuntime();

        await new Badge(js).SetAsync(7);

        Assert.Equal([7], js.ArgsFor("__raskBadge.set"));
    }

    [Fact]
    public async Task Set_WithoutCount_PassesNull()
    {
        var js = new FakeJsRuntime();

        await new Badge(js).SetAsync();

        Assert.Equal(new object?[] { null }, js.ArgsFor("__raskBadge.set"));
    }

    [Fact]
    public async Task Clear_CallsHelper()
    {
        var js = new FakeJsRuntime();

        await new Badge(js).ClearAsync();

        Assert.Equal(1, js.CallCount("__raskBadge.clear"));
    }
}
