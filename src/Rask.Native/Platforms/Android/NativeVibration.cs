using Android.App;
using Android.Content;
using Android.OS;
using Rask.Core.Browser;

namespace Rask.Native;

// Native Android backend for IVibration — Vibrator / VibratorManager (API 31+). Honors the full
// vibrate/pause pattern (unlike iOS). Registered by AndroidPlatform.
internal sealed class NativeVibration(Activity activity) : IVibration
{
    public ValueTask<bool> VibrateAsync(params int[] pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        var vibrator = GetVibrator();
        if (vibrator is null || !vibrator.HasVibrator || pattern.Length == 0)
        {
            return ValueTask.FromResult(false);
        }

        // Web pattern semantics are [vibrate, pause, vibrate, …]; Android waveform timings start with an OFF
        // segment, so prepend a 0-length wait to align them.
        var timings = new long[pattern.Length + 1];
        for (var i = 0; i < pattern.Length; i++)
        {
            timings[i + 1] = Math.Max(0, pattern[i]);
        }

        if (OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            vibrator.Vibrate(VibrationEffect.CreateWaveform(timings, -1));
        }
        else
        {
#pragma warning disable CA1422 // legacy vibrate for API < 26
            vibrator.Vibrate(timings, -1);
#pragma warning restore CA1422
        }

        return ValueTask.FromResult(true);
    }

    public ValueTask<bool> CancelAsync()
    {
        GetVibrator()?.Cancel();
        return ValueTask.FromResult(true);
    }

    private Vibrator? GetVibrator()
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(31))
        {
            var manager = (VibratorManager?)activity.GetSystemService(Context.VibratorManagerService);
            return manager?.DefaultVibrator;
        }

#pragma warning disable CA1422 // Context.VibratorService is the pre-31 path
        return (Vibrator?)activity.GetSystemService(Context.VibratorService);
#pragma warning restore CA1422
    }
}
