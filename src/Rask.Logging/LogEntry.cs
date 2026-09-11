using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
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

/// <summary>The EF Core mapping for <see cref="LogEntry"/>.</summary>
internal sealed class LogEntryConfiguration : IEntityTypeConfiguration<LogEntry>
{
    public void Configure(EntityTypeBuilder<LogEntry> entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        // The same table and index names as the file store's, so a query plan or a runbook reads the same on both.
        entity.ToTable("RaskLog");
        entity.HasKey(e => e.Id);
        entity.Property(e => e.Category).HasMaxLength(LogEntry.CategoryMaxLength).IsRequired();
        entity.Property(e => e.Message).IsRequired();

        // Retention's cutoff, and the time-range filter.
        entity.HasIndex(e => e.Timestamp);

        // The level and category filters, each already in the newest-first order a page is read in.
        entity.HasIndex(e => new { e.Level, e.Id }).IsDescending(false, true);
        entity.HasIndex(e => new { e.Category, e.Id }).IsDescending(false, true);
    }
}

/// <summary>Model-building helper for the log table.</summary>
public static class LoggingModelBuilderExtensions
{
    /// <summary>
    /// Maps the <c>RaskLog</c> table that <c>AddRaskLogging&lt;TContext&gt;()</c> writes to. Call from your
    /// context's <c>OnModelCreating</c>, then create the table with <c>rask db add AddLogs &amp;&amp; rask db update</c>.
    /// </summary>
    /// <remarks>
    /// Only for the application-database store. <c>AddRaskLogging()</c> keeps its log in a SQLite
    /// file of its own and needs nothing in your model.
    /// </remarks>
    /// <param name="modelBuilder">The model being built.</param>
    /// <returns>The same builder, for chaining.</returns>
    public static ModelBuilder AddRaskLogging(this ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.ApplyConfiguration(new LogEntryConfiguration());
        return modelBuilder;
    }
}
