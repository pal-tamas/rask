using Android.App;
using Rask.Core.Browser;

namespace Rask.Native;

// Native Android backend for IScreenInfo — the app's DisplayMetrics (Density = device-pixel ratio) instead
// of window.screen. Registered by AndroidPlatform. Uses Resources.DisplayMetrics (no deprecated Display API).
internal sealed class NativeScreenInfo(Activity activity) : IScreenInfo
{
    public ValueTask<ScreenInfo> GetAsync()
    {
        var dm = activity.Resources!.DisplayMetrics!;
        var density = dm.Density <= 0 ? 1f : dm.Density;
        // WidthPixels/HeightPixels are physical pixels; divide by density to get CSS pixels. Android has no
        // separate "available" area distinct from the app window, and no color-depth API (24 is universal).
        var cssWidth = (int)Math.Round(dm.WidthPixels / density);
        var cssHeight = (int)Math.Round(dm.HeightPixels / density);
        return ValueTask.FromResult(new ScreenInfo(cssWidth, cssHeight, cssWidth, cssHeight, 24, density));
    }
}
