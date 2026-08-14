using Rask.Core.Browser;

namespace Rask.Example.Shared.Features;

/// <summary><see cref="ISpeechSynthesis" /> — speak text aloud (text-to-speech).</summary>
public sealed partial class SpeechDemo(ISpeechSynthesis speech) : Component
{
    private string _text = "Hello from Rask — spoken straight from C#.";
    private string? _status;

    protected override Component? Render() =>
        BsCard.Class(Bs.Join(Shadow.Sm, Border.None))[
            BsCardBody[
                Input
                    .Value(_text)
                    .Id("speech-text")
                    .Class("form-control form-control-sm mb-2")
                    .OnInput(v => _text = v),
                BsStack.Gap(2).WrapItems(true).Class(Margin.Bottom(2))[
                    BsButton.Color(BsColor.Primary).Size(BsSize.Sm).Id("speech-speak").OnClickAsync(Speak)["Speak"],
                    BsButton
                        .Color(BsColor.Danger)
                        .Outline(true)
                        .Size(BsSize.Sm)
                        .Id("speech-cancel")
                        .OnClickAsync(Cancel)["Stop"]
                ],
                Div.Class("small text-secondary")["Status: ", Code.Id("speech-status")[_status ?? "(idle)"]]
            ]
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
