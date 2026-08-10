using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.JSInterop;
using Rask.Client.Browser;
using Rask.Core.Browser;
using UIKit;

namespace Rask.Native;

/// <summary>
///     The iOS platform module. Pass it to <see cref="NativeAppHost.UsePlatform" /> and the host wires these
///     native C# backends for the browser/device API interfaces — <c>IShare</c> (system share sheet),
///     <c>IGeolocation</c> (CoreLocation), <c>IClipboard</c> (UIPasteboard), <c>IVibration</c>,
///     <c>IWakeLock</c>, <c>INetworkInfo</c> (NWPathMonitor), <c>IBattery</c> (UIDevice battery monitoring),
///     <c>ISpeechSynthesis</c> (AVSpeechSynthesizer), <c>ISpeechRecognition</c> (SFSpeechRecognizer),
///     <c>IScreenInfo</c> (UIScreen), <c>IDeviceOrientation</c>/<c>IDeviceMotion</c> (CoreMotion),
///     <c>INotifications</c> (UNUserNotificationCenter), <c>IBadge</c> (the app-icon badge), and
///     <c>IPermissions</c> (the OS authorization status the backends above actually gate on) — so
///     injecting any of them resolves the native implementation, and every other interface falls back to the
///     WebView's JS. The app writes one line
///     (<c>host.UsePlatform(new ApplePlatform(() =&gt; Window?.RootViewController))</c>) instead of
///     registering each backend by hand.
/// </summary>
/// <param name="presenter">
///     Supplies the <see cref="UIViewController" /> the system share sheet presents from (typically the
///     window's root view controller). Evaluated lazily on each share.
/// </param>
public sealed class ApplePlatform(Func<UIViewController?> presenter) : INativePlatform
{
    /// <inheritdoc />
    public void Register(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        // TryAdd + direct construction: native-first (an explicit app registration still wins) and AOT-safe
        // (no reflection-based activation on the iOS full-AOT head).
        services.TryAddSingleton<IShare>(_ => new NativeShare(presenter));
        services.TryAddSingleton<IGeolocation>(_ => new NativeGeolocation());
        services.TryAddSingleton<IClipboard>(_ => new NativeClipboard());
        services.TryAddSingleton<IVibration>(_ => new NativeVibration());
        services.TryAddSingleton<IWakeLock>(_ => new NativeWakeLock());
        services.TryAddSingleton<INetworkInfo>(_ => new NativeNetworkInfo());
        services.TryAddSingleton<IBattery>(_ => new NativeBattery());
        services.TryAddSingleton<ISpeechSynthesis>(_ => new NativeSpeechSynthesis());
        services.TryAddSingleton<ISpeechRecognition>(_ => new NativeSpeechRecognition());
        services.TryAddSingleton<IScreenInfo>(_ => new NativeScreenInfo());
        services.TryAddSingleton<IDeviceOrientation>(_ => new NativeDeviceOrientation());
        services.TryAddSingleton<IDeviceMotion>(_ => new NativeDeviceMotion());
        services.TryAddSingleton<INotifications>(_ => new NativeNotifications());
        services.TryAddSingleton<IBadge>(_ => new NativeBadge());
        // Camera/Microphone defer to the WebView's own Permissions API (see NativePermissionQuery), so this
        // one needs it. Built directly rather than resolved as IPermissions — that would resolve to this.
        services.TryAddSingleton<IPermissions>(sp =>
            new NativePermissionQuery(new Permissions(sp.GetRequiredService<IJSRuntime>())));
    }
}
