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
///     (Vibrator), <c>IWakeLock</c> (FLAG_KEEP_SCREEN_ON), <c>INetworkInfo</c> (ConnectivityManager),
///     <c>IBattery</c> (BatteryManager), <c>ISpeechSynthesis</c> (TextToSpeech), <c>IScreenInfo</c> (DisplayMetrics),
///     <c>IDeviceOrientation</c>/<c>IDeviceMotion</c> (SensorManager), <c>INotifications</c>
///     (NotificationManager), and <c>IBadge</c> (a badge notification) — so injecting any of them resolves the
///     native implementation and every other interface falls back to the
///     WebView's JS. The app writes one line (<c>host.UsePlatform(new AndroidPlatform(this))</c>) instead of
///     registering each backend by hand. Geolocation needs <c>ACCESS_FINE_LOCATION</c>, network info needs
///     <c>ACCESS_NETWORK_STATE</c>, and notifications need <c>POST_NOTIFICATIONS</c> (API 33+) in the manifest.
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
        services.TryAddSingleton<IBattery>(_ => new NativeBattery(activity));
        services.TryAddSingleton<ISpeechSynthesis>(_ => new NativeSpeechSynthesis(activity));
        services.TryAddSingleton<IScreenInfo>(_ => new NativeScreenInfo(activity));
        services.TryAddSingleton<IDeviceOrientation>(_ => new NativeDeviceOrientation(activity));
        services.TryAddSingleton<IDeviceMotion>(_ => new NativeDeviceMotion(activity));
        services.TryAddSingleton<INotifications>(_ => new NativeNotifications(activity));
        services.TryAddSingleton<IBadge>(_ => new NativeBadge(activity));
    }
}
