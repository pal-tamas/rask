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
        (typeof(IBackgroundSync), typeof(BackgroundSync)),
    ];

    [Fact]
    public void AddWasmBrowserApis_registers_the_WASM_only_wrappers_as_singletons()
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
    public void AddWasmBrowserApis_registers_nothing_beyond_the_pinned_set()
    {
        var services = new ServiceCollection();

        services.AddWasmBrowserApis(ServiceLifetime.Singleton);

        var registered = services.Select(d => d.ServiceType).ToHashSet();
        var pinned = WasmOnlyApis.Select(a => a.Service).ToHashSet();

        Assert.Empty(registered.Except(pinned));   // registered but unpinned → add it to WasmOnlyApis
        Assert.Empty(pinned.Except(registered));   // pinned but unregistered → stale entry
    }

    // Every wrapper here goes in through AddBrowserApi's TryAdd, so an app that wants its own
    // implementation registers it first and keeps it.
    [Fact]
    public void AddWasmBrowserApis_is_fallback_only_so_an_app_supplied_background_sync_registered_first_wins()
    {
        var services = new ServiceCollection();

        services.AddSingleton<IBackgroundSync, FakeAppBackgroundSync>();
        services.AddWasmBrowserApis(ServiceLifetime.Singleton);

        var descriptor = Assert.Single(services, d => d.ServiceType == typeof(IBackgroundSync));
        Assert.Equal(typeof(FakeAppBackgroundSync), descriptor.ImplementationType);
    }

    private sealed class FakeAppBackgroundSync : IBackgroundSync
    {
        public ValueTask<bool> IsSupportedAsync() => ValueTask.FromResult(false);

        public ValueTask<bool> IsPeriodicSupportedAsync() => ValueTask.FromResult(false);

        public ValueTask<bool> RequestSyncAsync(string tag) => ValueTask.FromResult(false);

        public ValueTask<IReadOnlyList<string>> GetPendingTagsAsync() => ValueTask.FromResult<IReadOnlyList<string>>([]);

        public ValueTask<string> GetPeriodicPermissionAsync() => ValueTask.FromResult("denied");

        public ValueTask<bool> RequestPeriodicSyncAsync(string tag, TimeSpan minInterval) => ValueTask.FromResult(false);

        public ValueTask UnregisterPeriodicAsync(string tag) => default;

        public ValueTask<IReadOnlyList<string>> GetPeriodicTagsAsync() => ValueTask.FromResult<IReadOnlyList<string>>([]);

        public ValueTask<IAsyncDisposable> OnSyncAsync(Func<BackgroundSyncEvent, Task> onSync) =>
            throw new NotSupportedException();
    }
}
