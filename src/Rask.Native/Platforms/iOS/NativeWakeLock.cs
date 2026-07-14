using CoreFoundation;
using Rask.Core.Browser;
using UIKit;

namespace Rask.Native;

// Native iOS backend for IWakeLock — UIApplication.IdleTimerDisabled keeps the screen awake, instead of the
// Screen Wake Lock API that WKWebView restricts. Ref-counted so overlapping sentinels compose: the idle
// timer is re-enabled only when the last one is disposed. Registered by ApplePlatform.
internal sealed class NativeWakeLock : IWakeLock
{
    private static int _held;

    public ValueTask<bool> IsSupportedAsync() => ValueTask.FromResult(true);

    public ValueTask<IWakeLockSentinel> RequestAsync()
    {
        DispatchQueue.MainQueue.DispatchAsync(() =>
        {
            _held++;
            UIApplication.SharedApplication.IdleTimerDisabled = true;
        });
        return ValueTask.FromResult<IWakeLockSentinel>(new Sentinel());
    }

    private sealed class Sentinel : IWakeLockSentinel
    {
        private bool _released;

        public ValueTask DisposeAsync()
        {
            if (_released)
            {
                return default;
            }

            _released = true;
            DispatchQueue.MainQueue.DispatchAsync(() =>
            {
                if (--_held <= 0)
                {
                    _held = 0;
                    UIApplication.SharedApplication.IdleTimerDisabled = false;
                }
            });
            return default;
        }
    }
}
