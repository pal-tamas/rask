using System.Text.RegularExpressions;
using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

// GestureTrigger (and the typed FullscreenTrigger / EyeDropperTrigger) are headless like Shareable: they
// render whatever the Template returns and hand it the data-rask-gesture bundle. The shared client runs the
// capability inside the click gesture — so activation-gated APIs work even on the Server transport — and
// posts any result back through GestureResultInterop. No IJSRuntime, no host-specific registration.
public class GestureTriggerTests
{
    [Fact]
    public void FullscreenTrigger_StampsCapWithNullRid_DataAttrBeforeTagSpecific()
    {
        // Fire-and-forget (no result) → rid is null. Attribute order: data-* before tag-specific (type).
        Assert.Equal(
            "<button data-rask-gesture=\"{&quot;cap&quot;:&quot;fullscreen.request&quot;,&quot;rid&quot;:null}\" type=\"button\">Full screen</button>",
            FullscreenTrigger(g => Button(Type: "button", Data: g)["Full screen"]).ToHtml());
    }

    [Fact]
    public void GestureTrigger_GenericCapability_StampsTheGivenCap()
    {
        Assert.Equal(
            "<a data-rask-gesture=\"{&quot;cap&quot;:&quot;pip.request&quot;,&quot;rid&quot;:null}\" href=\"#\">PiP</a>",
            GestureTrigger(Capability: "pip.request", Template: g => A(Href: "#", Data: g)["PiP"]).ToHtml());
    }

    [Fact]
    public void EyeDropperTrigger_WithCallback_StampsCapAndANumericResultId()
    {
        var html = EyeDropperTrigger(
            OnColor: _ => Task.CompletedTask,
            Template: g => Button(Type: "button", Data: g)["Pick"]).ToHtml();

        Assert.Matches(@"data-rask-gesture=""\{&quot;cap&quot;:&quot;eyedropper\.open&quot;,&quot;rid&quot;:\d+\}""", html);
    }

    [Fact]
    public async Task GestureResultInterop_RoutesTheResultToTheTriggersCallback_ThenIsOneShot()
    {
        string? received = null;
        var html = EyeDropperTrigger(
            OnColor: value => { received = value; return Task.CompletedTask; },
            Template: g => Button(Type: "button", Data: g)["Pick"]).ToHtml();
        var rid = int.Parse(Regex.Match(html, @"rid&quot;:(\d+)").Groups[1].Value);

        await GestureResultInterop.Result(rid, "#ff8800");
        Assert.Equal("#ff8800", received);

        // One-shot: the handler is removed after the first result, so a second post is a no-op.
        received = null;
        await GestureResultInterop.Result(rid, "#000000");
        Assert.Null(received);
    }
}
