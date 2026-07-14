using Android.App;
using Android.Views;
using Rask.Core.Browser;

namespace Rask.Native;

// Native Android backend for IWakeLock — the Window FLAG_KEEP_SCREEN_ON (keeps the screen awake with no
// WAKE_LOCK permission) instead of the Screen Wake Lock API the WebView restricts. Ref-counted so
// overlapping sentinels compose. Registered by AndroidPlatform.
internal sealed class NativeWakeLock(Activity activity) : IWakeLock
{
    private int _held;

    public ValueTask<bool> IsSupportedAsync() => ValueTask.FromResult(true);

    public ValueTask<IWakeLockSentinel> RequestAsync()
    {
        activity.RunOnUiThread(() =>
        {
            _held++;
            activity.Window?.AddFlags(WindowManagerFlags.KeepScreenOn);
        });
        return ValueTask.FromResult<IWakeLockSentinel>(new Sentinel(this));
    }

    private void Release()
    {
        activity.RunOnUiThread(() =>
        {
            if (--_held <= 0)
            {
                _held = 0;
                activity.Window?.ClearFlags(WindowManagerFlags.KeepScreenOn);
            }
        });
    }

    private sealed class Sentinel(NativeWakeLock owner) : IWakeLockSentinel
    {
        private bool _released;

        public ValueTask DisposeAsync()
        {
            if (!_released)
            {
                _released = true;
                owner.Release();
            }

            return default;
        }
    }
}
