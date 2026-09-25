using System.Text.RegularExpressions;
using Rask.Core.Browser;

namespace Rask.Core.Tests.Components;

// Trigger.Gesture (and the typed Trigger.Fullscreen / Trigger.EyeDropper) are headless like Shareable: they
// render whatever the Template returns and hand it the data-rask-gesture bundle. The shared client runs the
// capability inside the click gesture — so activation-gated APIs work even on the Server transport — and
// posts any result back through GestureResultInterop. No IJSRuntime, no host-specific registration.
public partial class GestureTriggerTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Fullscreen_trigger_stamps_cap_with_null_rid_data_attr_before_tag_specific()
    {
        // Fire-and-forget (no result) → rid is null. Attribute order: data-* before tag-specific (type).
        Assert.Equal(
            "<button data-rask-gesture=\"{&quot;cap&quot;:&quot;fullscreen.request&quot;,&quot;rid&quot;:null}\" type=\"button\">Full screen</button>",
            Trigger.Fullscreen.Template(g => Button.Type("button").Data(g)["Full screen"]).ToHtml());
    }

    [Fact]
    public void Gesture_trigger_generic_capability_stamps_the_given_cap()
    {
        Assert.Equal(
            "<a data-rask-gesture=\"{&quot;cap&quot;:&quot;pip.request&quot;,&quot;rid&quot;:null}\" href=\"#\">PiP</a>",
            Trigger.Gesture.Capability("pip.request").Template(g => A.Href("#").Data(g)["PiP"]).ToHtml());
    }

    [Fact]
    public void Eye_dropper_trigger_with_callback_stamps_cap_and_a_numeric_result_id()
    {
        var html = Trigger.EyeDropper
            .Template(g => Button.Type("button").Data(g)["Pick"])
            .OnColor(_ => Task.CompletedTask).ToHtml();

        Assert.Matches(@"data-rask-gesture=""\{&quot;cap&quot;:&quot;eyedropper\.open&quot;,&quot;rid&quot;:\d+\}""", html);
    }

    [Fact]
    public async Task Gesture_result_interop_routes_the_result_to_the_triggers_callback_then_is_one_shot()
    {
        string? received = null;
        var html = Trigger.EyeDropper
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
    public void Screen_orientation_trigger_stamps_lock_cap_with_the_orientation_arg()
    {
        // Fire-and-forget (rid null) plus the optional `arg` (the orientation type) after cap/rid.
        Assert.Equal(
            "<button data-rask-gesture=\"{&quot;cap&quot;:&quot;orientation.lock&quot;,&quot;rid&quot;:null,"
            + "&quot;arg&quot;:&quot;landscape&quot;}\" type=\"button\">Rotate</button>",
            Trigger.ScreenOrientation
                .Orientation("landscape")
                .Template(g => Button.Type("button").Data(g)["Rotate"]).ToHtml());
    }

    [Fact]
    public void Picture_in_picture_trigger_stamps_pip_cap_with_the_target_video_ref_id()
    {
        var video = ElementRef.New();
        Assert.Equal(
            "<button data-rask-gesture=\"{&quot;cap&quot;:&quot;pip.request&quot;,&quot;rid&quot;:null,"
            + $"&quot;el&quot;:&quot;{video.Id}&quot;}}\" type=\"button\">Pop out</button>",
            Trigger.PictureInPicture
                .For(video)
                .Template(g => Button.Type("button").Data(g)["Pop out"]).ToHtml());
    }

    [Fact]
    public void Fullscreen_trigger_with_for_stamps_the_target_element_ref_id()
    {
        var box = ElementRef.New();
        Assert.Equal(
            "<button data-rask-gesture=\"{&quot;cap&quot;:&quot;fullscreen.request&quot;,&quot;rid&quot;:null,"
            + $"&quot;el&quot;:&quot;{box.Id}&quot;}}\" type=\"button\">Full screen</button>",
            Trigger.Fullscreen.Template(g => Button.Type("button").Data(g)["Full screen"]).For(box).ToHtml());
    }

    [Fact]
    public void Media_capture_trigger_stamps_media_start_cap_with_target_ref_and_constraints_arg()
    {
        var preview = ElementRef.New();
        var html = Trigger.MediaCapture
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
    public async Task Install_trigger_stamps_install_prompt_cap_and_routes_the_outcome_to_on_outcome()
    {
        string? outcome = null;
        var html = Trigger.Install
            .Template(g => Button.Type("button").Data(g)["Install"])
            .OnOutcome(value => { outcome = value; return Task.CompletedTask; }).ToHtml();

        Assert.Matches(@"data-rask-gesture=""\{&quot;cap&quot;:&quot;install\.prompt&quot;,&quot;rid&quot;:\d+\}""", html);

        var rid = int.Parse(Regex.Match(html, @"rid&quot;:(\d+)").Groups[1].Value);
        await GestureResultInterop.Result(rid, "accepted");
        Assert.Equal("accepted", outcome);
    }

    [Fact]
    public async Task Media_capture_trigger_hands_the_stream_id_to_on_stream_and_still_says_granted_to_on_result()
    {
        // The capability now resolves the stream's id instead of the literal "granted". OnResult must keep
        // its original vocabulary — the id is an addition, not a replacement.
        MediaStreamId? stream = null;
        string? result = null;
        var html = Trigger.MediaCapture
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
    public async Task Media_capture_trigger_a_refusal_reaches_on_result_only()
    {
        var streamed = false;
        string? result = null;
        var html = Trigger.MediaCapture
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
    public void Media_capture_trigger_with_no_callbacks_stays_fire_and_forget()
    {
        // No callback means no result to route, so no id should be registered — otherwise every render
        // leaks an entry into the process-wide gesture registry for nobody to consume.
        var html = Trigger.MediaCapture
            .For(ElementRef.New())
            .Template(g => Button.Type("button").Data(g)["Start camera"]).ToHtml();

        Assert.Contains("&quot;rid&quot;:null", html);
    }
}
