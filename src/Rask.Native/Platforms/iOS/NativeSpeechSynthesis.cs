using AVFoundation;
using Rask.Core.Browser;

namespace Rask.Native;

// Native iOS backend for ISpeechSynthesis — AVSpeechSynthesizer instead of the WebView's speechSynthesis
// (which is unreliable inside WKWebView). Registered by ApplePlatform. The synthesizer is held for the app
// lifetime so queued utterances survive across calls.
internal sealed class NativeSpeechSynthesis : ISpeechSynthesis
{
    private readonly AVSpeechSynthesizer _synth = new();

    public ValueTask<bool> IsSupportedAsync() => ValueTask.FromResult(true);

    public ValueTask SpeakAsync(string text, SpeechOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        var utterance = new AVSpeechUtterance(text);
        if (options?.Lang is { } lang)
        {
            utterance.Voice = AVSpeechSynthesisVoice.FromLanguage(lang);
        }

        if (options?.Rate is { } rate)
        {
            // Web rate is 0.1–10 (1 = normal); AVSpeechUtterance rate is [Min..Max] with ~0.5 the default.
            utterance.Rate = (float)Math.Clamp(
                rate * AVSpeechUtterance.DefaultSpeechRate,
                AVSpeechUtterance.MinimumSpeechRate,
                AVSpeechUtterance.MaximumSpeechRate);
        }

        if (options?.Pitch is { } pitch)
        {
            utterance.PitchMultiplier = (float)pitch;
        }

        if (options?.Volume is { } volume)
        {
            utterance.Volume = (float)volume;
        }

        _synth.SpeakUtterance(utterance);
        return default;
    }

    public ValueTask CancelAsync()
    {
        _synth.StopSpeaking(AVSpeechBoundary.Immediate);
        return default;
    }
}
