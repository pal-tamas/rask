using Android.App;
using Android.Runtime;
using Android.Speech.Tts;
using Java.Util;
using Rask.Core.Browser;

namespace Rask.Native;

// Native Android backend for ISpeechSynthesis — the platform TextToSpeech engine instead of the WebView's
// speechSynthesis. The engine initializes asynchronously; IsSupportedAsync/SpeakAsync await that. Registered
// by AndroidPlatform (held for the app lifetime).
internal sealed class NativeSpeechSynthesis : Java.Lang.Object, ISpeechSynthesis, TextToSpeech.IOnInitListener
{
    private readonly TextToSpeech _tts;
    private readonly TaskCompletionSource<bool> _ready = new();

    public NativeSpeechSynthesis(Activity activity) => _tts = new TextToSpeech(activity, this);

    public void OnInit([GeneratedEnum] OperationResult status) =>
        _ready.TrySetResult(status == OperationResult.Success);

    public ValueTask<bool> IsSupportedAsync() => new(_ready.Task);

    public async ValueTask SpeakAsync(string text, SpeechOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (!await _ready.Task)
        {
            return;
        }

        if (options?.Lang is { } lang)
        {
            _tts.SetLanguage(Locale.ForLanguageTag(lang));
        }

        if (options?.Rate is { } rate)
        {
            _tts.SetSpeechRate((float)rate);
        }

        if (options?.Pitch is { } pitch)
        {
            _tts.SetPitch((float)pitch);
        }

        // Queue behind anything already speaking, matching the web queueing behaviour.
        _tts.Speak(text, QueueMode.Add, null, Guid.NewGuid().ToString());
    }

    public ValueTask CancelAsync()
    {
        _tts.Stop();
        return default;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _tts.Shutdown();
        }

        base.Dispose(disposing);
    }
}
