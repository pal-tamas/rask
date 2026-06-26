using Rask.Wasm.Browser;

namespace Rask.Wasm.Tests.Browser;

public class ScreenOrientationTests
{
    [Fact]
    public async Task IsSupported_CallsHelper()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskOrientation.isSupported", true);

        Assert.True(await new ScreenOrientation(js).IsSupportedAsync());
    }

    [Theory]
    [InlineData("portrait-primary", OrientationType.PortraitPrimary)]
    [InlineData("portrait-secondary", OrientationType.PortraitSecondary)]
    [InlineData("landscape-primary", OrientationType.LandscapePrimary)]
    [InlineData("landscape-secondary", OrientationType.LandscapeSecondary)]
    [InlineData("something-new", OrientationType.Unknown)]
    [InlineData(null, OrientationType.Unknown)]
    public async Task Get_MapsTypeAndAngle(string? raw, OrientationType expected)
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskOrientation.get", new OrientationReading(raw, 90));

        var info = await new ScreenOrientation(js).GetAsync();

        Assert.Equal(expected, info.Type);
        Assert.Equal(90, info.Angle);
    }

    [Theory]
    [InlineData(OrientationLock.Any, "any")]
    [InlineData(OrientationLock.Natural, "natural")]
    [InlineData(OrientationLock.Portrait, "portrait")]
    [InlineData(OrientationLock.Landscape, "landscape")]
    [InlineData(OrientationLock.PortraitPrimary, "portrait-primary")]
    [InlineData(OrientationLock.LandscapeSecondary, "landscape-secondary")]
    public async Task Lock_PassesSpecName(OrientationLock orientation, string expected)
    {
        var js = new FakeJsRuntime();

        await new ScreenOrientation(js).LockAsync(orientation);

        Assert.Equal([expected], js.ArgsFor("__raskOrientation.lock"));
    }

    [Fact]
    public async Task Unlock_CallsHelper()
    {
        var js = new FakeJsRuntime();

        await new ScreenOrientation(js).UnlockAsync();

        Assert.Equal(1, js.CallCount("__raskOrientation.unlock"));
    }
}
