using Microsoft.JSInterop;

namespace Rask.Core.Browser;

/// <summary>An estimate of the origin's storage budget (<c>StorageManager.estimate()</c>).</summary>
/// <param name="Quota">Total bytes the origin may use (a conservative estimate).</param>
/// <param name="Usage">Bytes the origin is currently using across caches, IndexedDB, etc.</param>
public sealed record StorageEstimate(long Quota, long Usage)
{
    /// <summary>Fraction of the quota in use, <c>0</c>–<c>1</c> (<c>0</c> when the quota is unknown).</summary>
    public double UsageRatio => Quota > 0 ? (double)Usage / Quota : 0;
}

/// <summary>
///     Typed access to the Storage API's quota estimate
///     (<see href="https://developer.mozilla.org/en-US/docs/Web/API/StorageManager/estimate" />) — how much
///     on-device storage the origin may use and how much it already uses, e.g. to budget a cache or warn
///     before filling up — plus the same object's <b>persistence</b> knob, which asks for that storage to be
///     exempt from eviction. Pairs with <see cref="IBrowserStorage" />,
///     <see cref="IOriginPrivateFileSystem" />, and the offline/PWA story. Works on <b>both transports</b>;
///     inject it through a component constructor and read from an event handler or lifecycle hook.
/// </summary>
/// <remarks>
///     Requires a secure context; support is partial — gate on <see cref="IsSupportedAsync" />,
///     <see cref="EstimateAsync" /> returns <c>null</c> where the API is unavailable. The figures are
///     deliberately coarse (anti-fingerprinting), so treat them as a budget, not an exact count.
/// </remarks>
public interface IStorageEstimator
{
    /// <summary>Whether the browser exposes the estimate (<c>navigator.storage.estimate</c>).</summary>
    ValueTask<bool> IsSupportedAsync();

    /// <summary>Reads the current <see cref="StorageEstimate" />, or <c>null</c> when unsupported.</summary>
    ValueTask<StorageEstimate?> EstimateAsync();

    /// <summary>
    ///     Whether the origin's storage is already exempt from eviction
    ///     (<c>navigator.storage.persisted()</c>). <c>false</c> where unsupported.
    /// </summary>
    ValueTask<bool> IsPersistedAsync();

    /// <summary>
    ///     Asks for the origin's storage to be exempted from eviction, returning whether it now is
    ///     (<c>navigator.storage.persist()</c>). Chromium decides from engagement heuristics without
    ///     prompting; Firefox shows a permission prompt, so call this from a user-gesture handler. Already
    ///     being persisted resolves <c>true</c> without re-asking, and <c>false</c> where unsupported.
    /// </summary>
    ValueTask<bool> RequestPersistAsync();
}

/// <summary>
///     Default <see cref="IStorageEstimator" />, backed by the unified <see cref="IJSRuntime" />.
///     <c>navigator.storage.estimate()</c> resolves to a live object, so the read goes through the
///     framework's <c>__raskApi.storageEstimate</c> helper, which returns a plain <c>{ quota, usage }</c>
///     snapshot.
/// </summary>
public sealed class StorageEstimator(IJSRuntime js) : IStorageEstimator
{
    /// <inheritdoc />
    public ValueTask<bool> IsSupportedAsync() => js.InvokeAsync<bool>("__raskApi.storageSupported");

    /// <inheritdoc />
    public ValueTask<StorageEstimate?> EstimateAsync() =>
        js.InvokeAsync<StorageEstimate?>("__raskApi.storageEstimate");

    /// <inheritdoc />
    public ValueTask<bool> IsPersistedAsync() => js.InvokeAsync<bool>("__raskApi.storagePersisted");

    /// <inheritdoc />
    public ValueTask<bool> RequestPersistAsync() => js.InvokeAsync<bool>("__raskApi.storagePersist");
}
