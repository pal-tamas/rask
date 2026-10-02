using Rask.Core;
using Rask.Web.Types;

namespace Rask.Web.Tests;

// Live objects where a method takes one, element refs whose members answer with one, the element the browser names,
// and the statics of an event's class.
public sealed class WebLiveArgumentTests
{
    [Fact]
    public async Task A_kept_object_handed_to_a_method_crosses_as_its_handle()
    {
        var browser = new FakeBrowser();

        using (browser.Enter())
        {
            await using var utterance = await SpeechSynthesisUtterance.Create("Hello");
            await utterance.SetRate(1.2);
            await SpeechSynthesis.Speak(utterance);
        }

        Assert.Equal("""[["g","speechSynthesis"],["c","speak",[{"__raskArg__":0}]]]""", browser.Steps(2));
        Assert.Same(browser.Kept.Single(), browser.Calls[2].Args[2]);
    }

    [Fact]
    public async Task An_element_ref_runs_a_chain_from_its_element_and_keeps_what_it_answers_with()
    {
        var browser = new FakeBrowser();
        var video = new ElementRef<HTMLVideoElement>();

        using (browser.Enter())
        {
            await using var pip = await video.RequestPictureInPicture();
        }

        Assert.Equal("""[["e","",[{"__raskArg__":0}]],["c","requestPictureInPicture"]]""", browser.Steps(0));
        Assert.Same(video, browser.Calls[0].Args[2]);
        Assert.True(browser.Kept.Single().Disposed);
    }

    [Fact]
    public async Task An_animation_takes_its_keyframes_as_maps_of_CSS_properties_and_is_kept()
    {
        var browser = new FakeBrowser();
        var box = new ElementRef<HTMLDivElement>();

        using (browser.Enter())
        {
            await using var fade = await box.Animate([new() { ["opacity"] = 0 }, new() { ["opacity"] = 1 }], 300);
            await using var slide = await box.Animate([new() { ["transform"] = "translateX(0)" }], new KeyframeAnimationOptions { Duration = "300", Easing = "ease-out" });
        }

        Assert.Equal("""[["e","",[{"__raskArg__":0}]],["c","animate",[[{"opacity":0},{"opacity":1}],300]]]""", browser.Steps(0));
        Assert.Equal("""[["e","",[{"__raskArg__":0}]],["c","animate",[[{"transform":"translateX(0)"}],{"duration":300,"easing":"ease-out"}]]]""",
            browser.Steps(1));
    }

    [Fact]
    public async Task A_media_elements_srcObject_is_set_to_a_kept_stream_or_to_null()
    {
        var browser = new FakeBrowser();
        var video = new ElementRef<HTMLVideoElement>();

        using (browser.Enter())
        {
            await using var stream = await MediaStream.Create();
            await video.SetSrcObject(stream);
            await video.SetSrcObject(null);
        }

        Assert.Equal("""[["e","",[{"__raskArg__":0}]],["s","srcObject",[{"__raskArg__":1}]]]""", browser.Steps(1));
        Assert.Same(browser.Kept.Single(), browser.Calls[1].Args[3]);
        Assert.Equal("""[["e","",[{"__raskArg__":0}]],["s","srcObject",[null]]]""", browser.Steps(2));
    }

    [Fact]
    public async Task An_element_the_browser_names_reads_as_your_ref_to_it_or_null()
    {
        var stage = new ElementRef<HTMLDivElement>();
        var browser = new FakeBrowser().Answers($$"""{"__raskRef__":"{{stage.Id}}"}""", "null");

        ElementRef? current;
        ElementRef? after;
        using (browser.Enter())
        {
            current = await Document.FullscreenElement;
            after = await Document.FullscreenElement;
        }

        Assert.True(current == stage);
        Assert.Null(after);
        Assert.Equal("""[["g","document"],["g","fullscreenElement"]]""", browser.Steps(0));
    }

    [Fact]
    public async Task An_event_classs_static_is_on_its_class()
    {
        var browser = new FakeBrowser().Answers("\"granted\"");

        PermissionState state;
        using (browser.Enter())
        {
            state = await DeviceOrientationEvent.RequestPermission();
        }

        Assert.Equal(PermissionState.Granted, state);
        Assert.Equal("""[["g","DeviceOrientationEvent"],["c","requestPermission"]]""", browser.Steps(0));
    }
}
