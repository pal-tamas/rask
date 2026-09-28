using Microsoft.Extensions.Logging;

namespace Rask.Logging;

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
