using Rask.Core.Browser;

namespace Rask.Site.Features;

/// <summary><see cref="ISpeechSynthesis" /> — speak text aloud (text-to-speech).</summary>
public sealed partial class SpeechDemo(ISpeechSynthesis speech) : Component
{
    private string _text = "Hello from Rask — spoken straight from C#.";
    private string? _status;

    protected override Component? Render() =>
        UiCard.Class("shadow-sm")[
                UiInput
                    .Value(_text)
                    .Label("Text to speak")
                    .Id("speech-text")
                    .Class("mb-2")
                    .OnInput(v => _text = v),
                Div.Class("flex gap-2 flex-wrap items-center mb-2")[
                    UiButton.Tone(UiTone.Primary).Id("speech-speak").OnClick(Speak)["Speak"],
                    UiButton.Tone(UiTone.Error).Variant(UiVariant.Outline)
                        .Id("speech-cancel")
                        .OnClick(Cancel)["Stop"]
                ],
                Div.Class("text-sm text-ui-muted")["Status: ", Code.Id("speech-status")[_status ?? "(idle)"]]
            ];

    private async Task Speak()
    {
        try
        {
            if (!await speech.IsSupportedAsync())
            {
                _status = "Speech synthesis not supported in this browser";
                return;
            }

            await speech.SpeakAsync(_text, new SpeechOptions { Lang = "en-US", Rate = 1 });
            _status = "Speaking";
        }
        catch (Exception ex) { _status = "Failed: " + ex.Message; }
    }

    private async Task Cancel()
    {
        await speech.CancelAsync();
        _status = "Stopped";
    }
}
