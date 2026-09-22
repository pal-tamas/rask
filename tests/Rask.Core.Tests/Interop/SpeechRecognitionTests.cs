using Rask.Core.Browser;

namespace Rask.Core.Tests.Interop;

public class SpeechRecognitionTests
{
    [Fact]
    public async Task Asking_whether_speech_recognition_is_supported_calls_the_helper()
    {
        var js = new FakeJsRuntime();
        await new SpeechRecognition(js).IsSupportedAsync();

        Assert.Equal("__raskSpeechRecognition.isSupported", js.Calls.Single().Identifier);
    }

    [Fact]
    public async Task Starting_registers_the_handler_and_starts_under_an_id_with_the_options()
    {
        var js = new FakeJsRuntime();

        var session = await new SpeechRecognition(js).StartAsync(
            _ => Task.CompletedTask,
            new SpeechRecognitionOptions { Lang = "en-US", Continuous = true, InterimResults = true });

        Assert.NotNull(session);
        var args = js.ArgsFor("__raskSpeechRecognition.start");
        Assert.IsType<int>(args![0]);
        var options = Assert.IsType<SpeechRecognitionOptions>(args[1]);
        Assert.Equal("en-US", options.Lang);
        Assert.True(options.Continuous);
        Assert.True(options.InterimResults);
    }

    [Fact]
    public async Task Starting_defaults_the_options_when_null()
    {
        var js = new FakeJsRuntime();
        await new SpeechRecognition(js).StartAsync(_ => Task.CompletedTask);

        Assert.IsType<SpeechRecognitionOptions>(js.ArgsFor("__raskSpeechRecognition.start")![1]);
    }

    [Fact]
    public async Task A_result_is_routed_to_the_handler()
    {
        var js = new FakeJsRuntime();
        RecognitionResult? got = null;
        await new SpeechRecognition(js).StartAsync(r =>
        {
            got = r;
            return Task.CompletedTask;
        });
        var id = (int)js.ArgsFor("__raskSpeechRecognition.start")![0]!;

        await SpeechRecognitionInterop.Result(id, new RecognitionResult("hello world", true, 0.92));

        Assert.Equal(new RecognitionResult("hello world", true, 0.92), got);
    }

    [Fact]
    public async Task Disposing_stops_the_session_and_stops_routing()
    {
        var js = new FakeJsRuntime();
        var count = 0;
        var session = await new SpeechRecognition(js).StartAsync(_ =>
        {
            count++;
            return Task.CompletedTask;
        });
        var id = (int)js.ArgsFor("__raskSpeechRecognition.start")![0]!;

        await session.DisposeAsync();
        await SpeechRecognitionInterop.Result(id, new RecognitionResult("after", true, 1)); // unregistered → no-op

        Assert.Equal([id], js.ArgsFor("__raskSpeechRecognition.stop"));
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task A_result_for_an_unknown_id_does_nothing() =>
        await SpeechRecognitionInterop.Result(-42, new RecognitionResult("x", false, 0));

    [Fact]
    public async Task Starting_with_a_null_arg_throws() =>
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await new SpeechRecognition(new FakeJsRuntime()).StartAsync(null!));
}
