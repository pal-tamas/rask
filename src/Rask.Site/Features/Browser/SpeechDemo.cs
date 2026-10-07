using Microsoft.JSInterop;

namespace Rask.Site.Features;

/// <summary>MDN's <c>SpeechSynthesis</c> from Rask.Web — speak text aloud (text-to-speech) in the browser's own voices.</summary>
public sealed partial class SpeechDemo : Component
{
    private string _text = "Hello from Rask — spoken straight from C#.";
    private string? _status;

    protected override Component? Render() =>
        Ui.Card[
                Ui.Input
                    .Value(_text)
                    .Label("Text to speak")
                    .Id("speech-text")
                    .Class("mb-2")
                    .OnInput(v => _text = v),
                Div.Class("flex gap-2 flex-wrap items-center mb-2")[
                    Ui.Button.Primary.Id("speech-speak").OnClick(Speak)["Speak"],
                    Ui.Button.Red
                        .Id("speech-cancel")
                        .OnClick(Cancel)["Stop"]
                ],
                Div.Class("text-sm text-ui-muted")["Status: ", Code.Id("speech-status")[_status ?? "(idle)"]]
            ];

    private async Task Speak()
    {
        try
        {
            if (!await SpeechSynthesis.IsSupported)
            {
                _status = "Speech synthesis not supported in this browser";
                return;
            }

            await using var utterance = await SpeechSynthesisUtterance.Create(_text);
            await utterance.SetLang("en-US");
            await SpeechSynthesis.Speak(utterance);
            _status = "Speaking";
        }
        catch (JSException ex)
        {
            _status = "Failed: " + ex.Message;
        }
    }

    private async Task Cancel()
    {
        try
        {
            await SpeechSynthesis.Cancel();
            _status = "Stopped";
        }
        catch (JSException ex)
        {
            _status = "Failed: " + ex.Message;
        }
    }
}
