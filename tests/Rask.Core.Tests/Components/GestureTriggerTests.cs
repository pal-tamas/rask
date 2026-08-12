using System.Text.RegularExpressions;
using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

// GestureTrigger (and the typed FullscreenTrigger / EyeDropperTrigger) are headless like Shareable: they
// render whatever the Template returns and hand it the data-rask-gesture bundle. The shared client runs the
// capability inside the click gesture — so activation-gated APIs work even on the Server transport — and
// posts any result back through GestureResultInterop. No IJSRuntime, no host-specific registration.
public partial class GestureTriggerTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void FullscreenTrigger_StampsCapWithNullRid_DataAttrBeforeTagSpecific()
    {
        // Fire-and-forget (no result) → rid is null. Attribute order: data-* before tag-specific (type).
        Assert.Equal(
            "<button data-rask-gesture=\"{&quot;cap&quot;:&quot;fullscreen.request&quot;,&quot;rid&quot;:null}\" type=\"button\">Full screen</button>",
            FullscreenTrigger.Template(g => Button.Type("button").Data(g)["Full screen"]).ToHtml());
    }

    [Fact]
    public void GestureTrigger_GenericCapability_StampsTheGivenCap()
    {
        Assert.Equal(
            "<a data-rask-gesture=\"{&quot;cap&quot;:&quot;pip.request&quot;,&quot;rid&quot;:null}\" href=\"#\">PiP</a>",
            GestureTrigger.Capability("pip.request").Template(g => A.Href("#").Data(g)["PiP"]).ToHtml());
    }

    [Fact]
    public void EyeDropperTrigger_WithCallback_StampsCapAndANumericResultId()
    {
        var html = EyeDropperTrigger
            .Template(g => Button.Type("button").Data(g)["Pick"])
            .OnColor(_ => Task.CompletedTask).ToHtml();

        Assert.Matches(@"data-rask-gesture=""\{&quot;cap&quot;:&quot;eyedropper\.open&quot;,&quot;rid&quot;:\d+\}""", html);
    }

    [Fact]
    public async Task GestureResultInterop_RoutesTheResultToTheTriggersCallback_ThenIsOneShot()
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
    public void ScreenOrientationTrigger_StampsLockCapWithTheOrientationArg()
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
    public void PictureInPictureTrigger_StampsPipCapWithTheTargetVideoRefId()
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
    public void FullscreenTrigger_WithFor_StampsTheTargetElementRefId()
    {
        var box = ElementRef.New();
        Assert.Equal(
            "<button data-rask-gesture=\"{&quot;cap&quot;:&quot;fullscreen.request&quot;,&quot;rid&quot;:null,"
            + $"&quot;el&quot;:&quot;{box.Id}&quot;}}\" type=\"button\">Full screen</button>",
            FullscreenTrigger.Template(g => Button.Type("button").Data(g)["Full screen"]).For(box).ToHtml());
    }

    [Fact]
    public void MediaCaptureTrigger_StampsMediaStartCapWithTargetRefAndConstraintsArg()
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
    public async Task InstallTrigger_StampsInstallPromptCap_AndRoutesTheOutcomeToOnOutcome()
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
}
