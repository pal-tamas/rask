namespace Rask.Core.Browser;

/// <summary>
///     One entry from <see cref="IWebLocks.QueryAsync" /> — a lock that is currently held or waiting.
/// </summary>
/// <param name="Name">The lock name.</param>
/// <param name="Mode">Requested mode, <c>"exclusive"</c> or <c>"shared"</c> (the raw API string).</param>
/// <param name="ClientId">An opaque id for the browsing context that holds/requested it, when reported.</param>
/// <param name="Held"><c>true</c> if this lock is currently granted; <c>false</c> if it is still pending.</param>
public sealed record LockInfo(string Name, string Mode, string? ClientId, bool Held);
