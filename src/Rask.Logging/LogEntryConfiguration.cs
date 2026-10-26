using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Rask.Logging;

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

        // Unbounded on purpose, and SAID so: an app-wide string length (IRaskModel's Conventions) applies to every column
        // that states none, and would otherwise cut these off.
        foreach (var text in new[] { nameof(LogEntry.Message), nameof(LogEntry.Exception), nameof(LogEntry.Scopes) })
        {
            entity.Property(text).Metadata.SetMaxLength(null);
        }


        // Retention's cutoff, and the time-range filter.
        entity.HasIndex(e => e.Timestamp);

        // The level and category filters, each already in the newest-first order a page is read in.
        entity.HasIndex(e => new { e.Level, e.Id }).IsDescending(false, true);
        entity.HasIndex(e => new { e.Category, e.Id }).IsDescending(false, true);
    }
}
