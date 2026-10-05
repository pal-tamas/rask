using System.Text.Json;
using Rask.Core;
using Rask.Core.ScopedAssets;
using Rask.Web.Types;

namespace Rask.Web.Tests;

// What the browser hands over as data that is a list (a speech recogniser's results), and a stream a Rask service
// handed the app as a handle, which Rask.Web wraps as MDN's MediaStream.
public sealed class WebHandedOverTests
{
    [Fact]
    public async Task A_recognisers_results_cross_with_the_event_as_lists_read_by_index()
    {
        var browser = new FakeBrowser().Answers("3");
        var dictation = new Dictation();

        using (browser.Enter())
        {
            await dictation.Listen();
        }

        var listen = browser.Calls.Single(c => c.Identifier == "__raskWeb.listen");
        await ScopedScript.Invoke(
            ((ScopedScript.ScriptCallback)listen.Args[4]!).Id,
            JsonDocument.Parse("""
                [{"resultIndex":1,"results":{"items":[
                    {"isFinal":true,"items":[{"transcript":"old","confidence":0.5}]},
                    {"isFinal":true,"items":[{"transcript":"hello","confidence":0.9},{"transcript":"yellow","confidence":0.1}]}]}}]
                """).RootElement);

        Assert.StartsWith("""["resultIndex","results",""", (string)listen.Args[3]!, StringComparison.Ordinal);
        Assert.Equal(("hello", 0.9, 2, 2, true), (dictation.Heard, dictation.Confidence, dictation.Results, dictation.Alternatives, dictation.Final));
    }

    [Fact]
    public async Task A_stream_a_Rask_service_handed_over_is_MDNs_MediaStream_run_from_its_handle()
    {
        var browser = new FakeBrowser().Answers("[]");
        var handed = new FakeBrowser.FakeHandle();

        using (browser.Enter())
        {
            var camera = MediaStream.From(handed);
            await camera.GetTracks();
            await camera.DisposeAsync();
        }

        Assert.Equal(("""[["c","getTracks"]]""", handed), (browser.Steps(0), browser.Calls[0].Args[0]));
        Assert.True(handed.Disposed);
    }

    private sealed class Dictation : Component
    {
        public string Heard { get; private set; } = "";

        public double Confidence { get; private set; }

        public int Results { get; private set; }

        public int Alternatives { get; private set; }

        public bool Final { get; private set; }

        public async Task Listen()
        {
            var recognition = await SpeechRecognition.Create();
            await recognition.OnResult(e =>
            {
                var latest = e.Results[e.ResultIndex];
                (Heard, Confidence) = (latest[0].Transcript, latest[0].Confidence);
                (Results, Alternatives, Final) = (e.Results.Length, latest.Length, latest.IsFinal);
            });
        }

        protected override Component? Render() => null;
    }
}
