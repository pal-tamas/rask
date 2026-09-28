namespace Rask.Wasm.Browser;

/// <summary>One sync the browser woke the app up for.</summary>
/// <param name="Tag">The tag the app registered — how you tell your syncs apart.</param>
/// <param name="Periodic">
///     <see langword="false" /> for a one-shot connectivity sync (<c>SyncManager</c>),
///     <see langword="true" /> for a recurring one (<c>PeriodicSyncManager</c>).
/// </param>
public sealed record BackgroundSyncEvent(string Tag, bool Periodic);
