using Rask.Core.Browser;

namespace Rask.Core.Tests.Interop;

public class WebAnimationsTests
{
    private static readonly Dictionary<string, string[]> Fade = new() { ["opacity"] = ["0", "1"] };

    [Fact]
    public async Task IsSupported_CallsHelper()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskAnim.supported", true);

        Assert.True(await new WebAnimations(js).IsSupportedAsync());
    }

    [Fact]
    public async Task Start_ReturnsAHandleAndPassesTheKeyframesAndTiming()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskAnim.start", 7);

        var id = await new WebAnimations(js).StartAsync(
            ElementRef.New(), Fade, new AnimationOptions(DurationMs: 250, Easing: "ease-out"));

        Assert.Equal(new AnimationId(7), id);
        Assert.True(id.IsValid);

        var call = Assert.Single(js.Calls, c => c.Identifier == "__raskAnim.start");
        // A concrete Dictionary, not the interface — the JSON context registers the concrete shape and
        // the trimmed WASM publish depends on staying on it.
        Assert.IsType<Dictionary<string, string[]>>(call.Args![1]);
        Assert.Equal(new AnimationOptions(DurationMs: 250, Easing: "ease-out"), call.Args[2]);
    }

    [Fact]
    public async Task Start_OnABrowserWithoutTheApi_YieldsAnInvalidHandleRatherThanThrowing()
    {
        // No response registered → the helper returns 0. Starting is inert, which is what lets a caller
        // animate unconditionally without feature-testing first.
        var js = new FakeJsRuntime();

        var id = await new WebAnimations(js).StartAsync(ElementRef.New(), Fade);

        Assert.False(id.IsValid);
    }

    [Fact]
    public async Task Start_DefaultsTheTimingWhenNoOptionsAreGiven()
    {
        var js = new FakeJsRuntime();

        await new WebAnimations(js).StartAsync(ElementRef.New(), Fade);

        var call = Assert.Single(js.Calls, c => c.Identifier == "__raskAnim.start");
        Assert.Equal(new AnimationOptions(), call.Args![2]);
    }

    [Fact]
    public async Task Start_RejectsNulls()
    {
        var js = new WebAnimations(new FakeJsRuntime());

        await Assert.ThrowsAsync<ArgumentNullException>(async () => await js.StartAsync(null!, Fade));
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await js.StartAsync(ElementRef.New(), null!));
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("finish")]
    [InlineData("pause")]
    [InlineData("play")]
    public async Task ControlMethods_PassTheRawHandle(string verb)
    {
        var js = new FakeJsRuntime();
        var anims = new WebAnimations(js);
        var id = new AnimationId(3);

        await (verb switch
        {
            "cancel" => anims.CancelAsync(id),
            "finish" => anims.FinishAsync(id),
            "pause" => anims.PauseAsync(id),
            _ => anims.PlayAsync(id)
        });

        var call = Assert.Single(js.Calls, c => c.Identifier == $"__raskAnim.{verb}");
        Assert.Equal(3, call.Args![0]);
    }

    [Fact]
    public async Task Wait_IsFalseWhenCancelled_AndDoesNotThrow()
    {
        // The whole point of returning bool rather than letting `finished` reject: a cancelled animation
        // is an ordinary outcome, so awaiting it needs no try/catch at the call site.
        var js = new FakeJsRuntime();
        js.SetResponse("__raskAnim.finished", false);

        Assert.False(await new WebAnimations(js).WaitAsync(new AnimationId(1)));
    }

    [Fact]
    public async Task Wait_IsTrueWhenItRanToCompletion()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskAnim.finished", true);

        Assert.True(await new WebAnimations(js).WaitAsync(new AnimationId(1)));
    }
}
