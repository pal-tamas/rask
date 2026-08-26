using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Browser;
using Rask.Wasm.Browser;

namespace Rask.Wasm.Tests.Browser;

// Covers the WASM-only set (Rask.Wasm) — the wrappers that need a live document/handle, a device chooser or
// a transient user gesture. Together with the Core tier (Rask.Core.Tests) this pins the full 48-wrapper
// surface and its Singleton lifetime on the WASM host. IShare joined this set when Rask.Client was folded
// in: it was never a third tier, only a WASM-only wrapper kept in its own assembly for a second host that
// no longer exists.
public class RaskWasmBrowserApisTests
{
    private static readonly (Type Service, Type Impl)[] WasmOnlyApis =
    [
        (typeof(IShare), typeof(Share)),
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
    public void AddWasmBrowserApis_RegistersTheThirteenWasmOnlyWrappers_AsSingletons()
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

    // Every wrapper here goes in through AddBrowserApi's TryAdd, so an app that wants its own
    // implementation registers it first and keeps it. Pinned on IShare because that is the one an app is
    // most likely to replace, and because the assertion moved here with it from Rask.Client.Tests.
    [Fact]
    public void AddWasmBrowserApis_IsFallbackOnly_AnAppSuppliedShareRegisteredFirstWins()
    {
        var services = new ServiceCollection();

        services.AddSingleton<IShare, FakeAppShare>();
        services.AddWasmBrowserApis(ServiceLifetime.Singleton);

        var descriptor = Assert.Single(services, d => d.ServiceType == typeof(IShare));
        Assert.Equal(typeof(FakeAppShare), descriptor.ImplementationType);
    }

    private sealed class FakeAppShare : IShare
    {
        public ValueTask ShareAsync(ShareData data) => default;

        public ValueTask<bool> CanShareAsync(ShareData? data = null) => ValueTask.FromResult(true);
    }
}
