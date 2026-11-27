using Microsoft.Extensions.Logging;

namespace Rask.Logging;

/// <summary>One stored log entry.</summary>
/// <param name="Id">
/// The entry's id — the stable key for a list row, since timestamps collide: a version-7 <see cref="Guid" />, made by
/// Rask as the entry is written to the application's database, and derived from the row's place in a SQLite file
/// store's own file. <see cref="Guid.Empty" /> on an entry that has not been persisted yet (the value the logger hands
/// to the writer).
/// </param>
/// <param name="Timestamp">When it was logged (UTC).</param>
/// <param name="Level">Its severity.</param>
/// <param name="Category">The logger category, e.g. <c>Rask.Live</c>.</param>
/// <param name="EventId">The <see cref="Microsoft.Extensions.Logging.EventId"/>'s numeric id, or 0.</param>
/// <param name="Message">The formatted message.</param>
/// <param name="Exception">The exception's <c>ToString()</c>, if one was attached.</param>
/// <param name="Scopes">
/// The ambient <see cref="ILogger.BeginScope{TState}"/> state the entry was written under — the request id,
/// the user id, whatever correlation id the app opened a scope with — flattened outermost-first, or
/// <c>null</c> when no scope was open. This is what makes a stored log answer <em>"what else happened on
/// that request?"</em> rather than leaving it to be reconstructed from message text.
/// </param>
public sealed record LogRecord(
    Guid Id,
    DateTimeOffset Timestamp,
    LogLevel Level,
    string Category,
    int EventId,
    string Message,
    string? Exception,
    IReadOnlyList<LogScopeValue>? Scopes = null);
