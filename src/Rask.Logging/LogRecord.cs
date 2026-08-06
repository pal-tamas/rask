using Microsoft.Extensions.Logging;

namespace Rask.Logging;

/// <summary>One stored log entry.</summary>
/// <param name="Id">
/// The store's monotonic row id — the stable key for a list row, since timestamps collide. Zero on an entry
/// that has not been persisted yet (the value the logger hands to the writer); the store assigns the real id.
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
    long Id,
    DateTimeOffset Timestamp,
    LogLevel Level,
    string Category,
    int EventId,
    string Message,
    string? Exception,
    IReadOnlyList<LogScopeValue>? Scopes = null);

/// <summary>One key/value pair captured from an open logging scope.</summary>
/// <param name="Key">
/// The state key, e.g. <c>RequestId</c>. A scope opened with a bare object rather than key/value state
/// (<c>BeginScope("checkout")</c>) is stored under <see cref="LogScopeValue.MessageKey"/>.
/// </param>
/// <param name="Value">The value, already converted to a string at the call site.</param>
public readonly record struct LogScopeValue(string Key, string Value)
{
    /// <summary>The key a scope with no structured state is stored under.</summary>
    public const string MessageKey = "Scope";
}

/// <summary>
/// A filter over the stored log. Every property is optional; leaving them all unset asks for the newest page
/// of everything.
/// </summary>
public sealed record LogQuery
{
    /// <summary>Only entries at or above this level.</summary>
    public LogLevel? MinimumLevel { get; init; }

    /// <summary>Only entries whose category contains this substring (case-insensitive).</summary>
    public string? Category { get; init; }

    /// <summary>Only entries whose message or exception contains this substring (case-insensitive).</summary>
    public string? Search { get; init; }

    /// <summary>
    /// Only entries captured under a scope with this key, e.g. <c>RequestId</c>. Combine with
    /// <see cref="ScopeValue"/> to pin one request; on its own it finds every entry that carried the key.
    /// </summary>
    public string? ScopeKey { get; init; }

    /// <summary>
    /// Only entries whose <see cref="ScopeKey"/> holds this value. Ignored unless <see cref="ScopeKey"/> is
    /// set — a value without a key would match the same string appearing under any key, which is a
    /// different (and much less useful) question.
    /// </summary>
    public string? ScopeValue { get; init; }

    /// <summary>Only entries logged at or after this instant.</summary>
    public DateTimeOffset? From { get; init; }

    /// <summary>Only entries logged at or before this instant.</summary>
    public DateTimeOffset? To { get; init; }

    /// <summary>The 1-based page number. Values below 1 are treated as 1.</summary>
    public int Page { get; init; } = 1;

    /// <summary>How many entries a page holds. Default 50.</summary>
    public int PageSize { get; init; } = 50;
}

/// <summary>One page of query results, plus the total the filter matched.</summary>
/// <param name="Entries">The matching entries, newest first.</param>
/// <param name="TotalCount">How many entries match the filter in total, across every page.</param>
/// <param name="Page">The 1-based page these entries came from.</param>
/// <param name="PageSize">The page size the query ran with.</param>
public sealed record LogPage(IReadOnlyList<LogRecord> Entries, long TotalCount, int Page, int PageSize)
{
    /// <summary>An empty page, for a store with nothing in it yet.</summary>
    public static LogPage Empty(int page, int pageSize) => new([], 0, page, pageSize);

    /// <summary>How many pages the filter spans, at least 1.</summary>
    public int PageCount => TotalCount <= 0 ? 1 : (int)((TotalCount + PageSize - 1) / PageSize);
}
