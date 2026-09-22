using Rask.Core.Browser;

namespace Rask.Core.Tests.Interop;

public class SpeechSynthesisTests
{
    [Fact]
    public async Task Asking_whether_speech_synthesis_is_supported_calls_the_helper()
    {
        var js = new FakeJsRuntime();
        js.SetResponse("__raskApi.speechSupported", true);

        Assert.True(await new SpeechSynthesis(js).IsSupportedAsync());
    }

    [Fact]
    public async Task Speaking_sends_the_text_and_options()
    {
        var js = new FakeJsRuntime();
        var opts = new SpeechOptions { Lang = "en-US", Rate = 1.2 };

        await new SpeechSynthesis(js).SpeakAsync("hello", opts);

        Assert.Equal(["hello", opts], js.ArgsFor("__raskApi.speak"));
    }

    [Fact]
    public async Task Speaking_defaults_the_options_when_null()
    {
        var js = new FakeJsRuntime();

        await new SpeechSynthesis(js).SpeakAsync("hello");

        var args = js.ArgsFor("__raskApi.speak");
        Assert.Equal("hello", args![0]);
        Assert.IsType<SpeechOptions>(args[1]);
    }

    [Fact]
    public async Task Speaking_null_text_throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await new SpeechSynthesis(new FakeJsRuntime()).SpeakAsync(null!));
    }

    [Fact]
    public async Task Cancelling_speech_calls_the_helper()
    {
        var js = new FakeJsRuntime();

        await new SpeechSynthesis(js).CancelAsync();

        Assert.Equal(1, js.CallCount("__raskApi.cancelSpeech"));
    }
}
