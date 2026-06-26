using Rask.Core.Routing;

namespace Rask.Example.Shared.Features;

/// <summary>Browser APIs section — <see cref="SpeechDemo" /> (<c>ISpeechSynthesis</c>).</summary>
[Route("browser/speech")]
[ParentRoute(typeof(ShowcaseLayout))]
public sealed class SpeechPage : Component
{
    protected override RenderResult Head => Title()["Speech — Browser APIs — Rask"];

    protected override RenderResult Render() =>
    [
        PageHeader.Render(
            "Speech",
            "Speak text aloud from C# via ISpeechSynthesis (the SpeechSynthesis API) — for accessibility or "
            + "audible notifications. Works on both transports; trigger it from a user gesture."),
        CodeSample(
            ["SpeechDemo.cs"],
            Notes: "new SpeechSynthesisUtterance(...) is built in the framework's __raskApi.speak helper; "
                + "SpeakAsync queues the utterance, CancelAsync stops and clears the queue.",
            Result: SpeechDemo())
    ];
}
