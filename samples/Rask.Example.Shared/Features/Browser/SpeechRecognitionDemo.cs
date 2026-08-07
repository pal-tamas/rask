using Rask.Core.Browser;

namespace Rask.Example.Shared.Features;

/// <summary>
///     <see cref="ISpeechRecognition" /> — dictation: start listening, and each recognised phrase is pushed
///     to the handler (final phrases accumulate; the interim hypothesis shows live). Prompts for microphone
///     access on start; browser support is Chromium-only (gate on <see cref="ISpeechRecognition.IsSupportedAsync" />),
///     and in the native shell it resolves to a real OS backend. The handler updates state and calls
///     <c>StateHasChanged()</c> — the sanctioned pattern for an externally-pushed update.
/// </summary>
public sealed partial class SpeechRecognitionDemo(ISpeechRecognition recognition) : Component, IAsyncDisposable
{
    private IAsyncDisposable? _session;
    private string _transcript = "";
    private string _interim = "";
    private string _status = "(idle)";

    private bool Listening => _session is not null;

    protected override Component? Render() =>
        BsCard(Class: Bs.Join(Shadow.Sm, Border.None))[
            BsCardBody()[
                BsStack(Gap: 2, WrapItems: true, Class: Margin.Bottom(2))[
                    BsButton(Color: BsColor.Primary, Size: BsSize.Sm, Id: "speech-recognize-start",
                        Disabled: Listening, OnClickAsync: Start)["Start listening"],
                    BsButton(Color: BsColor.Danger, Outline: true, Size: BsSize.Sm, Id: "speech-recognize-stop",
                        Disabled: !Listening, OnClickAsync: Stop)["Stop"]
                ],
                Div(Class: "small text-secondary mb-1")[
                    "Transcript: ",
                    Code(Id: "speech-recognize-transcript")[_transcript.Length == 0 ? "(none)" : _transcript],
                    _interim.Length == 0 ? (Component?)null : Span(Class: "text-secondary fst-italic")[" ", _interim]
                ],
                Div(Class: "small text-secondary")["Status: ", Code(Id: "speech-recognize-status")[_status]]
            ]
        ];

    private async Task Start()
    {
        if (!await recognition.IsSupportedAsync())
        {
            _status = "not supported on this browser";
            return;
        }

        _transcript = "";
        _interim = "";
        _status = "listening…";
        try
        {
            _session = await recognition.StartAsync(
                r =>
                {
                    if (r.IsFinal)
                    {
                        _transcript = (_transcript + " " + r.Transcript).Trim();
                        _interim = "";
                    }
                    else
                    {
                        _interim = r.Transcript;
                    }

                    StateHasChanged();
                    return Task.CompletedTask;
                },
                new SpeechRecognitionOptions { Continuous = true, InterimResults = true });
        }
        catch (Exception ex)
        {
            _status = "failed: " + ex.Message;
        }
    }

    private new async Task Stop()
    {
        if (_session is not null)
        {
            await _session.DisposeAsync();
            _session = null;
        }

        _interim = "";
        _status = "stopped";
    }

    public async ValueTask DisposeAsync()
    {
        if (_session is not null)
        {
            await _session.DisposeAsync();
        }
    }
}
