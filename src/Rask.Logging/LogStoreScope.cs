namespace Rask.Logging;

/// <summary>
/// Marks the code a log store is running, so what that code logs is not captured back into the store.
/// </summary>
/// <remarks>
/// <para>
/// The file store never needed this: it talks to <c>Microsoft.Data.Sqlite</c> directly, and that category is excluded
/// outright. The application-database store runs on EF Core, and EF Core logs every command it sends at
/// <c>Information</c> — so each flush would write lines that the next flush stores, whose INSERT logs lines the flush
/// after that stores, for as long as the app runs.
/// </para>
/// <para>
/// Excluding EF Core's categories would stop that, and would also throw away the application's own SQL log, which is
/// exactly what an operator reads during an incident. An async-local marks the store's own flow instead: EF Core logs
/// on the calling flow, so everything it logs while a store operation is running is skipped, and nothing else is.
/// Drivers that log from their own threads (a connection pool's pruning) are covered by
/// <see cref="RaskLoggingOptions"/>'s always-excluded categories.
/// </para>
/// </remarks>
internal static class LogStoreScope
{
    private static readonly AsyncLocal<bool> Inside = new();

    /// <summary>Whether the current flow is inside a store operation.</summary>
    internal static bool Active => Inside.Value;

    /// <summary>Marks the current flow until the returned value is disposed.</summary>
    internal static Entered Enter()
    {
        var previous = Inside.Value;
        Inside.Value = true;
        return new Entered(previous);
    }

    /// <summary>Restores the marker to what it was before <see cref="Enter"/>.</summary>
    internal readonly struct Entered(bool previous) : IDisposable
    {
        public void Dispose() => Inside.Value = previous;
    }
}
