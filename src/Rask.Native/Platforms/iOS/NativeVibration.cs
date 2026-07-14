using AudioToolbox;
using Rask.Core.Browser;

namespace Rask.Native;

// Native iOS backend for IVibration. iOS exposes no arbitrary vibration-pattern API to third-party apps and
// WKWebView has no navigator.vibrate at all, so map any non-empty pattern to the system vibration
// (AudioToolbox kSystemSoundID_Vibrate). Precise pattern timings and cancellation aren't expressible on iOS.
internal sealed class NativeVibration : IVibration
{
    public ValueTask<bool> VibrateAsync(params int[] pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        if (pattern.Length == 0 || (pattern.Length == 1 && pattern[0] == 0))
        {
            return ValueTask.FromResult(false);
        }

        SystemSound.Vibrate.PlaySystemSound();
        return ValueTask.FromResult(true);
    }

    // iOS has no API to cancel an in-progress system vibration.
    public ValueTask<bool> CancelAsync() => ValueTask.FromResult(false);
}
