namespace Rask.Querying;

/// <summary>Whether a request is on the wire.</summary>
public enum FetchStatus
{
    /// <summary>Nothing in flight.</summary>
    Idle,

    /// <summary>A request is in flight, whether it is the first or a refresh.</summary>
    Fetching,

    /// <summary>
    ///     Would fetch, but must not. Today that means <see cref="QueryOptions.Enabled" /> is false —
    ///     a query waiting on something the user has not chosen yet. Offline will land here too.
    /// </summary>
    Paused,
}
