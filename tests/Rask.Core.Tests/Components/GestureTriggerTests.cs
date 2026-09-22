using System.Text.RegularExpressions;
using Rask.Core.Browser;
using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

// GestureTrigger (and the typed FullscreenTrigger / EyeDropperTrigger) are headless like Shareable: they
// render whatever the Template returns and hand it the data-rask-gesture bundle. The shared client runs the
// capability inside the click gesture — so activation-gated APIs work even on the Server transport — and
// posts any result back through GestureResultInterop. No IJSRuntime, no host-specific registration.
public partial class GestureTriggerTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void The_fullscreen_trigger_stamps_its_cap_with_a_null_rid_ahead_of_the_tag_attributes()
    {
        // Fire-and-forget (no result) → rid is null. Attribute order: data-* before tag-specific (type).
        Assert.Equal(
            "<button data-rask-gesture=\"{&quot;cap&quot;:&quot;fullscreen.request&quot;,&quot;rid&quot;:null}\" type=\"button\">Full screen</button>",
            FullscreenTrigger.Template(g => Button.Type("button").Data(g)["Full screen"]).ToHtml());
    }

    [Fact]
    public void A_generic_gesture_trigger_stamps_the_given_capability()
    {
        Assert.Equal(
            "<a data-rask-gesture=\"{&quot;cap&quot;:&quot;pip.request&quot;,&quot;rid&quot;:null}\" href=\"#\">PiP</a>",
            GestureTrigger.Capability("pip.request").Template(g => A.Href("#").Data(g)["PiP"]).ToHtml());
    }

    [Fact]
    public void An_eye_dropper_trigger_with_a_callback_stamps_its_cap_and_a_numeric_result_id()
    {
        var html = EyeDropperTrigger
            .Template(g => Button.Type("button").Data(g)["Pick"])
            .OnColor(_ => Task.CompletedTask).ToHtml();

        Assert.Matches(@"data-rask-gesture=""\{&quot;cap&quot;:&quot;eyedropper\.open&quot;,&quot;rid&quot;:\d+\}""", html);
    }

    [Fact]
    public async Task A_gesture_result_reaches_the_triggers_callback_only_once()
    {
        string? received = null;
        var html = EyeDropperTrigger
            .Template(g => Button.Type("button").Data(g)["Pick"])
            .OnColor(value => { received = value; return Task.CompletedTask; }).ToHtml();
        var rid = int.Parse(Regex.Match(html, @"rid&quot;:(\d+)").Groups[1].Value);

        await GestureResultInterop.Result(rid, "#ff8800");

        Assert.Equal("#ff8800", received);

        // One-shot: the handler is removed after the first result, so a second post is a no-op.
        received = null;
        await GestureResultInterop.Result(rid, "#000000");
        Assert.Null(received);
    }

    [Fact]
    public void The_orientation_trigger_stamps_the_lock_cap_with_the_orientation_argument()
    {
        // Fire-and-forget (rid null) plus the optional `arg` (the orientation type) after cap/rid.
        Assert.Equal(
            "<button data-rask-gesture=\"{&quot;cap&quot;:&quot;orientation.lock&quot;,&quot;rid&quot;:null,"
            + "&quot;arg&quot;:&quot;landscape&quot;}\" type=\"button\">Rotate</button>",
            ScreenOrientationTrigger
                .Orientation("landscape")
                .Template(g => Button.Type("button").Data(g)["Rotate"]).ToHtml());
    }

    [Fact]
    public void The_picture_in_picture_trigger_stamps_its_cap_with_the_target_video_ref_id()
    {
        var video = ElementRef.New();

        Assert.Equal(
            "<button data-rask-gesture=\"{&quot;cap&quot;:&quot;pip.request&quot;,&quot;rid&quot;:null,"
            + $"&quot;el&quot;:&quot;{video.Id}&quot;}}\" type=\"button\">Pop out</button>",
            PictureInPictureTrigger
                .For(video)
                .Template(g => Button.Type("button").Data(g)["Pop out"]).ToHtml());
    }

    [Fact]
    public void A_fullscreen_trigger_with_For_stamps_the_target_element_ref_id()
    {
        var box = ElementRef.New();

        Assert.Equal(
            "<button data-rask-gesture=\"{&quot;cap&quot;:&quot;fullscreen.request&quot;,&quot;rid&quot;:null,"
            + $"&quot;el&quot;:&quot;{box.Id}&quot;}}\" type=\"button\">Full screen</button>",
            FullscreenTrigger.Template(g => Button.Type("button").Data(g)["Full screen"]).For(box).ToHtml());
    }

    [Fact]
    public void The_media_capture_trigger_stamps_its_cap_with_the_target_ref_and_constraints()
    {
        var preview = ElementRef.New();
        var html = MediaCaptureTrigger
            .For(preview)
            .Template(g => Button.Type("button").Data(g)["Start camera"])
            .Video(true)
            .FacingMode("user").ToHtml();

        Assert.Contains("&quot;cap&quot;:&quot;media.start&quot;", html);
        Assert.Contains($"&quot;el&quot;:&quot;{preview.Id}&quot;", html);
        // The constraints ride in `arg` as an embedded JSON string ({ audio, video, facingMode }).
        Assert.Contains("audio", html);
        Assert.Contains("video", html);
        Assert.Contains("user", html);
    }

    [Fact]
    public async Task The_install_trigger_stamps_its_cap_and_routes_the_outcome_to_OnOutcome()
    {
        string? outcome = null;
        var html = InstallTrigger
            .Template(g => Button.Type("button").Data(g)["Install"])
            .OnOutcome(value => { outcome = value; return Task.CompletedTask; }).ToHtml();

        Assert.Matches(@"data-rask-gesture=""\{&quot;cap&quot;:&quot;install\.prompt&quot;,&quot;rid&quot;:\d+\}""", html);

        var rid = int.Parse(Regex.Match(html, @"rid&quot;:(\d+)").Groups[1].Value);
        await GestureResultInterop.Result(rid, "accepted");

        Assert.Equal("accepted", outcome);
    }

    [Fact]
    public async Task The_media_capture_trigger_hands_the_stream_id_to_OnStream_and_still_says_granted_to_OnResult()
    {
        // The capability now resolves the stream's id instead of the literal "granted". OnResult must keep
        // its original vocabulary — the id is an addition, not a replacement.
        MediaStreamId? stream = null;
        string? result = null;
        var html = MediaCaptureTrigger
            .For(ElementRef.New())
            .Template(g => Button.Type("button").Data(g)["Start camera"])
            .OnStream(id => { stream = id; return Task.CompletedTask; })
            .OnResult(value => { result = value; return Task.CompletedTask; }).ToHtml();

        var rid = int.Parse(Regex.Match(html, @"rid&quot;:(\d+)").Groups[1].Value);
        await GestureResultInterop.Result(rid, "12");

        Assert.Equal(new MediaStreamId(12), stream);
        Assert.Equal("granted", result);
    }

    [Fact]
    public async Task A_media_capture_refusal_reaches_only_OnResult()
    {
        var streamed = false;
        string? result = null;
        var html = MediaCaptureTrigger
            .For(ElementRef.New())
            .Template(g => Button.Type("button").Data(g)["Start camera"])
            .OnStream(_ => { streamed = true; return Task.CompletedTask; })
            .OnResult(value => { result = value; return Task.CompletedTask; }).ToHtml();

        var rid = int.Parse(Regex.Match(html, @"rid&quot;:(\d+)").Groups[1].Value);
        await GestureResultInterop.Result(rid, "denied");

        Assert.False(streamed);
        Assert.Equal("denied", result);
    }

    [Fact]
    public void A_media_capture_trigger_with_no_callbacks_stays_fire_and_forget()
    {
        // No callback means no result to route, so no id should be registered — otherwise every render
        // leaks an entry into the process-wide gesture registry for nobody to consume.
        var html = MediaCaptureTrigger
            .For(ElementRef.New())
            .Template(g => Button.Type("button").Data(g)["Start camera"]).ToHtml();

        Assert.Contains("&quot;rid&quot;:null", html);
    }
}
