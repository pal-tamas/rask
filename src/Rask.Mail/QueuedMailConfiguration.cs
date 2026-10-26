using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Rask.Mailing;

/// <summary>The EF Core mapping for <see cref="QueuedMail"/>.</summary>
public sealed class QueuedMailConfiguration : IEntityTypeConfiguration<QueuedMail>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<QueuedMail> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        // Not versioned and never soft-deleted, mapped with or without ApplyRaskConventions.
        builder.Ignore(x => x.Version);
        builder.Ignore(x => x.DeletedAt);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.From).IsRequired();
        builder.Property(x => x.To).IsRequired();
        builder.Property(x => x.Subject).IsRequired();

        // Unbounded on purpose, and SAID so: an app-wide string length (IRaskModel's Conventions) applies to every column
        // that states none, and would otherwise cut these off.
        foreach (var text in new[] { nameof(QueuedMail.From), nameof(QueuedMail.To), nameof(QueuedMail.Cc), nameof(QueuedMail.Bcc), nameof(QueuedMail.ReplyTo), nameof(QueuedMail.Subject), nameof(QueuedMail.TextBody), nameof(QueuedMail.HtmlBody), nameof(QueuedMail.Attachments), nameof(QueuedMail.Error) })
        {
            builder.Property(text).Metadata.SetMaxLength(null);
        }

        // Drives the "due, oldest first" claim query. ClaimedUntil stays out of it: in a healthy queue
        // almost every candidate row is unclaimed, so it costs nothing as a residual filter, and a
        // filtered index would need provider-specific SQL.
        builder.HasIndex(x => new { x.ProcessedAt, x.RunAt, x.Id });
        // Fences the completion write — see Job.ClaimToken for why.
        builder.Property(x => x.ClaimToken).IsConcurrencyToken();

        // Mapped EXPLICITLY, and the table is deliberately not PerTenant. A partitioned table takes a
        // query filter, and a filter here would hide other tenants' rows from the drain — the runner has to
        // see everybody's work. So the tenant is data on the row, not a partition of the table, and
        // ApplyRaskConventions leaves a tenant somebody mapped themselves alone.
        builder.Property(x => x.TenantId);
    }
}
