using Android.App;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Rask.Client.Browser;
using Rask.Core.Browser;

namespace Rask.Native;

/// <summary>
///     The Android platform module. Pass it to <see cref="NativeAppHost.UsePlatform" /> and the host wires
///     these native C# backends for the browser/device API interfaces — <c>IShare</c> (ACTION_SEND chooser),
///     <c>IGeolocation</c> (LocationManager), <c>IClipboard</c> (ClipboardManager), <c>IVibration</c>
///     (Vibrator), <c>IWakeLock</c> (FLAG_KEEP_SCREEN_ON), and <c>INetworkInfo</c> (ConnectivityManager) — so
///     injecting any of them resolves the native implementation and every other interface falls back to the
///     WebView's JS. The app writes one line (<c>host.UsePlatform(new AndroidPlatform(this))</c>) instead of
///     registering each backend by hand. Geolocation needs <c>ACCESS_FINE_LOCATION</c> and network info needs
///     <c>ACCESS_NETWORK_STATE</c> in the manifest.
/// </summary>
/// <param name="activity">The host <see cref="Activity" /> the backends run against (UI thread, services).</param>
public sealed class AndroidPlatform(Activity activity) : INativePlatform
{
    /// <inheritdoc />
    public void Register(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        // TryAdd + direct construction: native-first (an explicit app registration still wins) and trim-safe.
        services.TryAddSingleton<IShare>(_ => new NativeShare(activity));
        services.TryAddSingleton<IGeolocation>(_ => new NativeGeolocation(activity));
        services.TryAddSingleton<IClipboard>(_ => new NativeClipboard(activity));
        services.TryAddSingleton<IVibration>(_ => new NativeVibration(activity));
        services.TryAddSingleton<IWakeLock>(_ => new NativeWakeLock(activity));
        services.TryAddSingleton<INetworkInfo>(_ => new NativeNetworkInfo(activity));
    }
}
