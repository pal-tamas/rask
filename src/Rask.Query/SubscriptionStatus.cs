namespace Rask.Query;

/// <summary>Where a <see cref="Subscription{T}" /> is: opening, open, reopening, finished or refused.</summary>
public enum SubscriptionStatus
{
    /// <summary>Opening, and not yet admitted — the only state that warrants a spinner.</summary>
    Connecting,

    /// <summary>Open: every new value arrives as it happens.</summary>
    Live,

    /// <summary>The connection dropped and is being reopened; <c>Data</c> still holds the last value.</summary>
    Reconnecting,

    /// <summary>A function stream ran to its end. A notification subscription never ends by itself.</summary>
    Ended,

    /// <summary>Refused or broken for good — not allowed, or a key the server will never accept; see <c>Error</c>.</summary>
    Error,
}
