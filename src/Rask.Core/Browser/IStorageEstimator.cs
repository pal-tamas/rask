namespace Rask.Core.Browser;

/// <summary>
///     Typed access to the Storage API's quota estimate
///     (<see href="https://developer.mozilla.org/en-US/docs/Web/API/StorageManager/estimate" />) — how much
///     on-device storage the origin may use and how much it already uses, e.g. to budget a cache or warn
///     before filling up — plus the same object's <b>persistence</b> knob, which asks for that storage to be
///     exempt from eviction. Pairs with <c>LocalStorage</c>,
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
