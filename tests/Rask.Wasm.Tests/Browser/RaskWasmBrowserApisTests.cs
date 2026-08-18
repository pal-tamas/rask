using Microsoft.Extensions.DependencyInjection;
using Rask.Wasm.Browser;

namespace Rask.Wasm.Tests.Browser;

// Covers the WASM-only device/handle set (Rask.Wasm). Together with the Core tier (Rask.Core.Tests) and the
// in-process IShare tier (Rask.Client.Tests) this pins the full 47-wrapper surface and its Singleton lifetime
// on the WASM host.
public class RaskWasmBrowserApisTests
{
    private static readonly (Type Service, Type Impl)[] WasmOnlyApis =
    [
        (typeof(IFullscreen), typeof(Fullscreen)),
        (typeof(IScreenOrientation), typeof(ScreenOrientation)),
        (typeof(IEyeDropper), typeof(EyeDropper)),
        (typeof(IPictureInPicture), typeof(PictureInPicture)),
        (typeof(IIdleDetector), typeof(IdleDetectorService)),
        (typeof(IMediaDevices), typeof(MediaDevices)),
        (typeof(IInstallPrompt), typeof(InstallPrompt)),
        (typeof(ISerial), typeof(Serial)),
        (typeof(IUsb), typeof(Usb)),
        (typeof(IHid), typeof(Hid)),
        (typeof(IBluetooth), typeof(Bluetooth)),
        (typeof(IBackgroundSync), typeof(BackgroundSync)),
    ];

    [Fact]
    public void AddWasmBrowserApis_RegistersTheTwelveWasmOnlyWrappers_AsSingletons()
    {
        var services = new ServiceCollection();

        services.AddWasmBrowserApis(ServiceLifetime.Singleton);

        foreach (var (service, impl) in WasmOnlyApis)
        {
            var descriptor = Assert.Single(services, d => d.ServiceType == service);
            Assert.Equal(impl, descriptor.ImplementationType);
            Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
        }
    }

    // The test above only iterates WasmOnlyApis, so a wrapper missing from the list would never be checked
    // — the Core tier's equivalent list had silently fallen three behind its registrar. Compare against what
    // AddWasmBrowserApis actually registered, so adding a wrapper without pinning it fails here.
    [Fact]
    public void AddWasmBrowserApis_RegistersNothingBeyondThePinnedSet()
    {
        var services = new ServiceCollection();

        services.AddWasmBrowserApis(ServiceLifetime.Singleton);

        var registered = services.Select(d => d.ServiceType).ToHashSet();
        var pinned = WasmOnlyApis.Select(a => a.Service).ToHashSet();

        Assert.Empty(registered.Except(pinned));   // registered but unpinned → add it to WasmOnlyApis
        Assert.Empty(pinned.Except(registered));   // pinned but unregistered → stale entry
    }
}
