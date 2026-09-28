using Microsoft.JSInterop;

namespace Rask.Core.Browser;

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
