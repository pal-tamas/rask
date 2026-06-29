using Rask.Core.Browser;

namespace Rask.Example.Shared.Features;

/// <summary><see cref="ISpeechSynthesis" /> — speak text aloud (text-to-speech).</summary>
public sealed class SpeechDemo(ISpeechSynthesis speech) : Component
{
    private string _text = "Hello from Rask — spoken straight from C#.";
    private string? _status;

    protected override RenderResult Render() =>
        BsCard(Class: Bs.Join(Shadow.Sm, Border.None))[
            BsCardBody()[
                Input(
                    Id: "speech-text",
                    Class: "form-control form-control-sm mb-2",
                    Value: _text,
                    OnInput: v => _text = v),
                Div(Class: "d-flex gap-2 flex-wrap mb-2")[
                    Button(Class: "btn btn-primary btn-sm", Id: "speech-speak", OnClickAsync: Speak)["Speak"],
                    Button(Class: "btn btn-outline-danger btn-sm", Id: "speech-cancel", OnClickAsync: Cancel)["Stop"]
                ],
                Div(Class: "small text-secondary")["Status: ", Code(Id: "speech-status")[_status ?? "(idle)"]]
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
