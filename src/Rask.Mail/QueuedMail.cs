using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Rask.Mail;

/// <summary>
/// A persisted, ready-to-send email awaiting (or having completed) delivery. Written by
/// <see cref="IMailQueue"/> and drained by the <see cref="MailProcessor{TContext}"/>. Recipient lists and
/// attachments are stored as JSON; the body is already rendered to HTML at enqueue time. (Named
/// <c>QueuedMail</c> rather than <c>MailMessage</c> to avoid clashing with <c>System.Net.Mail.MailMessage</c>,
/// since this package ships a global <c>using Rask.Mail</c>.)
/// </summary>
public sealed class QueuedMail
{
    /// <summary>Database-generated, monotonically increasing key — the tiebreak for send order.</summary>
    public long Id { get; set; }

    /// <summary>The sender address, as JSON (see <see cref="MailSerializer"/>).</summary>
    public string From { get; set; } = "";

    /// <summary>The <c>To</c> recipients, as a JSON array.</summary>
    public string To { get; set; } = "";

    /// <summary>The <c>Cc</c> recipients, as a JSON array (or <c>null</c>).</summary>
    public string? Cc { get; set; }

    /// <summary>The <c>Bcc</c> recipients, as a JSON array (or <c>null</c>).</summary>
    public string? Bcc { get; set; }

    /// <summary>The <c>Reply-To</c> address, as JSON (or <c>null</c>).</summary>
    public string? ReplyTo { get; set; }

    /// <summary>The subject line.</summary>
    public string Subject { get; set; } = "";

    /// <summary>The HTML body, if any.</summary>
    public string? HtmlBody { get; set; }

    /// <summary>The <c>text/plain</c> body, if any.</summary>
    public string? TextBody { get; set; }

    /// <summary>The attachments, as a JSON array (or <c>null</c>).</summary>
    public string? Attachments { get; set; }

    /// <summary>The earliest time (UTC) the email is eligible to send — enqueue time, or later for a delayed send or a backed-off retry.</summary>
    public DateTime RunAt { get; set; }

    /// <summary>When the email was sent successfully (UTC), or <c>null</c> while it is pending.</summary>
    public DateTime? ProcessedAt { get; set; }

    /// <summary>How many times delivery has been attempted.</summary>
    public int Attempts { get; set; }

    /// <summary>The last failure message, if any.</summary>
    public string? Error { get; set; }

    /// <summary>When the email was enqueued (UTC).</summary>
    public DateTime CreatedAt { get; set; }
}

/// <summary>The EF Core mapping for <see cref="QueuedMail"/>.</summary>
public sealed class QueuedMailConfiguration : IEntityTypeConfiguration<QueuedMail>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<QueuedMail> entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        entity.HasKey(x => x.Id);
        entity.Property(x => x.From).IsRequired();
        entity.Property(x => x.To).IsRequired();
        entity.Property(x => x.Subject).IsRequired();
        // Drives the "due, oldest first" claim query.
        entity.HasIndex(x => new { x.ProcessedAt, x.RunAt, x.Id });
    }
}

/// <summary>Model-building helper for the mail table.</summary>
public static class MailModelBuilderExtensions
{
    /// <summary>
    /// Maps the <see cref="QueuedMail"/> table. Call from your context's <c>OnModelCreating</c>, then create
    /// the schema with <c>rask db add AddMail &amp;&amp; rask db update</c>.
    /// </summary>
    public static ModelBuilder AddRaskMail(this ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.ApplyConfiguration(new QueuedMailConfiguration());
        return modelBuilder;
    }
}
