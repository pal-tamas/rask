using Microsoft.JSInterop;

namespace Rask.Site.Features;

/// <summary>
///     MDN's <c>SpeechRecognition</c> from Rask.Web — dictation: start listening, and each <c>result</c> event hands the
///     handler the results as data (final phrases accumulate; the interim hypothesis shows live). Prompts for
///     microphone access on start. Chromium and Safari still ship it as <c>webkitSpeechRecognition</c>, which Rask.Web
///     finds under its MDN name. The handlers are lambdas in this component, so they re-render it.
/// </summary>
public sealed partial class SpeechRecognitionDemo : Component
{
    private Types.SpeechRecognition? _recognition;
    private readonly List<IAsyncDisposable> _subscriptions = [];
    private string _transcript = "";
    private string _interim = "";
    private string _status = "(idle)";

    private bool Listening => _recognition is not null;

    protected override Component? Render() =>
        Ui.Card.Class("shadow-sm")[
                Div.Class("flex gap-2 flex-wrap items-center mb-2")[
                    Ui.Button.Primary
                        .Id("speech-recognize-start")
                        .Disabled(Listening)
                        .OnClick(Start)["Start listening"],
                    Ui.Button.Error.Outline
                        .Id("speech-recognize-stop")
                        .Disabled(!Listening)
                        .OnClick(Stop)["Stop"]
                ],
                Div.Class("text-sm text-ui-muted mb-1")[
                    "Transcript: ",
                    Code.Id("speech-recognize-transcript")[_transcript.Length == 0 ? "(none)" : _transcript],
                    _interim.Length == 0 ? (Component?)null : Span.Class("text-ui-muted italic")[" ", _interim]
                ],
                Div.Class("text-sm text-ui-muted")["Status: ", Code.Id("speech-recognize-status")[_status]]
            ];

    private async Task Start()
    {
        if (!await SpeechRecognition.IsSupported)
        {
            _status = "not supported on this browser";
            return;
        }

        _transcript = "";
        _interim = "";
        try
        {
            _recognition = await SpeechRecognition.Create();
            await _recognition.SetContinuous(true);
            await _recognition.SetInterimResults(true);
            _subscriptions.Add(await _recognition.OnResult(e => Heard(e)));
            _subscriptions.Add(await _recognition.OnError(e => _status = "error: " + e.Error));
            _subscriptions.Add(await _recognition.OnEnd(async () => await Stop()));
            await _recognition.Start();
            _status = "listening…";
        }
        catch (JSException ex)
        {
            _status = "failed: " + ex.Message;
            await Release();
        }
    }

    // The results from ResultIndex on are new: a final one joins the transcript, an interim one shows as it is heard.
    private void Heard(Types.SpeechRecognitionEvent e)
    {
        var heard = e.Results.Items.Skip(e.ResultIndex).ToList();
        foreach (var phrase in heard.Where(r => r.IsFinal))
        {
            _transcript = (_transcript + " " + phrase[0].Transcript).Trim();
        }

        _interim = string.Concat(heard.Where(r => !r.IsFinal).Select(r => r[0].Transcript));
    }

    private async Task Stop()
    {
        if (_recognition is not null)
        {
            await _recognition.Stop();
            await Release();
            _status = "stopped";
        }

        _interim = "";
    }

    private async Task Release()
    {
        foreach (var subscription in _subscriptions)
        {
            await subscription.DisposeAsync();
        }

        _subscriptions.Clear();
        if (_recognition is not null)
        {
            await _recognition.DisposeAsync();
            _recognition = null;
        }
    }

    protected override Task OnUnmount() => Stop();
}
