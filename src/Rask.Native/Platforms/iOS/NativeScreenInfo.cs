using Rask.Core.Browser;
using UIKit;

namespace Rask.Native;

// Native iOS backend for IScreenInfo — UIScreen (true device metrics + scale) instead of window.screen.
// Registered by ApplePlatform. Read on the main thread.
internal sealed class NativeScreenInfo : IScreenInfo
{
    public ValueTask<ScreenInfo> GetAsync()
    {
        var tcs = new TaskCompletionSource<ScreenInfo>();
        UIApplication.SharedApplication.InvokeOnMainThread(() =>
        {
            var screen = UIScreen.MainScreen;
            var bounds = screen.Bounds; // points == CSS pixels
            var width = (int)Math.Round(bounds.Width);
            var height = (int)Math.Round(bounds.Height);
            // iOS has no per-app "available" area distinct from the screen, and no color-depth API (24 is the
            // universal effective value); Scale is the device-pixel ratio (2/3 on retina).
            tcs.TrySetResult(new ScreenInfo(width, height, width, height, 24, screen.Scale));
        });
        return new ValueTask<ScreenInfo>(tcs.Task);
    }
}
