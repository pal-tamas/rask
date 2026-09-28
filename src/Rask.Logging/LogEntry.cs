using Microsoft.Extensions.Logging;

namespace Rask.Logging;

/// <summary>
/// One row of the <c>RaskLog</c> table in the application's database — the storage shape behind
/// <see cref="LogRecord"/> for <c>AddRaskLogging&lt;TContext&gt;()</c>.
/// </summary>
/// <remarks>
/// Internal on purpose. <see cref="ILogs"/> is the query surface; a public entity would invite queries that skip
/// its paging bound, its case-insensitive search and its scope matching, and pin the column layout as API.
/// </remarks>
internal sealed class LogEntry
{
    /// <summary>
    /// The longest category stored. Long enough for any real logger category (a type's full name), and short
    /// enough to index on every provider — SQL Server's non-clustered key limit is 1,700 bytes.
    /// </summary>
    internal const int CategoryMaxLength = 512;

    public long Id { get; set; }

    /// <summary>When it was logged, as UTC.</summary>
    public DateTime Timestamp { get; set; }

    public LogLevel Level { get; set; }

    public string Category { get; set; } = "";

    public int EventId { get; set; }

    public string Message { get; set; } = "";

    public string? Exception { get; set; }

    /// <summary>The captured scope state as a JSON object, see <see cref="LogScopeJson"/>.</summary>
    public string? Scopes { get; set; }

    internal static LogEntry From(LogRecord record) => new()
    {
        Timestamp = record.Timestamp.UtcDateTime,
        Level = record.Level,
        // Cut rather than refused: an over-long category would fail the INSERT, and a failed INSERT loses the
        // whole batch it belongs to — every other line in that flush included.
        Category = LogText.WithoutNul(
            record.Category.Length > CategoryMaxLength ? record.Category[..CategoryMaxLength] : record.Category),
        EventId = record.EventId,
        Message = LogText.WithoutNul(record.Message),
        Exception = record.Exception is null ? null : LogText.WithoutNul(record.Exception),
        Scopes = LogScopeJson.Encode(record.Scopes),
    };

    internal LogRecord ToRecord() => new(
        Id,
        // SQLite hands a DateTime back as Unspecified; every value written here was UTC.
        new DateTimeOffset(DateTime.SpecifyKind(Timestamp, DateTimeKind.Utc)),
        Level,
        Category,
        EventId,
        Message,
        Exception,
        LogScopeJson.Decode(Scopes));
}
