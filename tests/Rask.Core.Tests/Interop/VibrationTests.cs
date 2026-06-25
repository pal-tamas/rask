using Rask.Core.Browser;

namespace Rask.Core.Tests.Interop;

public class VibrationTests
{
    [Fact]
    public async Task Vibrate_SendsPatternAsSingleArrayArg()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("navigator.vibrate", true);
        var vibration = new Vibration(js);

        var ok = await vibration.VibrateAsync(200, 100, 200);

        Assert.True(ok);
        // The pattern must arrive as one array argument: navigator.vibrate([200,100,200]).
        var args = js.ArgsFor("navigator.vibrate");
        Assert.NotNull(args);
        Assert.Single(args!);
        Assert.Equal(new[] { 200, 100, 200 }, Assert.IsType<int[]>(args![0]));
    }

    [Fact]
    public async Task Cancel_SendsZero()
    {
        var js = new FakeJsRuntime();
        var vibration = new Vibration(js);

        await vibration.CancelAsync();

        Assert.Equal([0], js.ArgsFor("navigator.vibrate"));
    }
}
