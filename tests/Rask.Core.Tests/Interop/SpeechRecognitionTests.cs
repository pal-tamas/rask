using Rask.Core.Browser;

namespace Rask.Core.Tests.Interop;

public class SpeechRecognitionTests
{
    [Fact]
    public async Task IsSupported_CallsHelper()
    {
        var js = new FakeJsRuntime();
        await new SpeechRecognition(js).IsSupportedAsync();
        Assert.Equal("__raskSpeechRecognition.isSupported", js.Calls.Single().Identifier);
    }

    [Fact]
    public async Task Start_RegistersHandler_AndStartsUnderAnId_WithOptions()
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
    public async Task Start_DefaultsOptions_WhenNull()
    {
        var js = new FakeJsRuntime();
        await new SpeechRecognition(js).StartAsync(_ => Task.CompletedTask);
        Assert.IsType<SpeechRecognitionOptions>(js.ArgsFor("__raskSpeechRecognition.start")![1]);
    }

    [Fact]
    public async Task Result_RoutesToHandler()
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
    public async Task Dispose_StopsSession_AndStopsRouting()
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
    public async Task Result_UnknownId_IsNoOp() =>
        await SpeechRecognitionInterop.Result(-42, new RecognitionResult("x", false, 0));

    [Fact]
    public async Task Start_NullArg_Throws() =>
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await new SpeechRecognition(new FakeJsRuntime()).StartAsync(null!));
}
