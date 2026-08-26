using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Browser;

namespace Rask.Wasm.Browser;

/// <summary>
///     Registration for the WASM-only browser/device wrappers — the ones that need a live document/handle, a
///     transient user gesture, an installed-PWA instance, or a device chooser, all of which only the in-browser
///     WASM host can provide. Server never registers these.
/// </summary>
public static class RaskWasmBrowserApis
{
    /// <summary>Registers the WASM-only wrappers at <paramref name="lifetime" /> (the WASM host uses Singleton).</summary>
    public static IServiceCollection AddWasmBrowserApis(this IServiceCollection services, ServiceLifetime lifetime)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddBrowserApi<IFullscreen, Fullscreen>(lifetime);
        services.AddBrowserApi<IScreenOrientation, ScreenOrientation>(lifetime);
        services.AddBrowserApi<IEyeDropper, EyeDropper>(lifetime);
        services.AddBrowserApi<IPictureInPicture, PictureInPicture>(lifetime);
        services.AddBrowserApi<IIdleDetector, IdleDetectorService>(lifetime);
        services.AddBrowserApi<IMediaDevices, MediaDevices>(lifetime);
        services.AddBrowserApi<IInstallPrompt, InstallPrompt>(lifetime);
        services.AddBrowserApi<ISerial, Serial>(lifetime);
        services.AddBrowserApi<IUsb, Usb>(lifetime);
        services.AddBrowserApi<IHid, Hid>(lifetime);
        services.AddBrowserApi<IBluetooth, Bluetooth>(lifetime);
        services.AddBrowserApi<IBackgroundSync, BackgroundSync>(lifetime);
        return services;
    }
}
