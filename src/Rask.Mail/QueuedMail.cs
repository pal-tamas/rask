using Rask.Data;

namespace Rask.Mailing;

/// <summary>
/// A persisted, ready-to-send email awaiting (or having completed) delivery. Written by
/// <see cref="IMail"/> and drained by the <see cref="MailProcessor{TContext}"/>. Recipient lists and
/// attachments are stored as JSON; the body is already rendered to HTML at enqueue time. (Named
/// <c>QueuedMail</c> rather than <c>MailMessage</c> to avoid clashing with <c>System.Net.Mail.MailMessage</c>,
/// since this package ships a global <c>using Rask.Mailing</c>.)
/// </summary>
/// <remarks>
/// An <see cref="Entity{TId}"/> rather than an <see cref="Aggregate{TId}"/>: an aggregate's soft-delete query
/// filter would hide rows from the send query and turn the dashboard's purge into a stamp, and its
/// <c>Version</c> would be a second concurrency token beside <see cref="ClaimToken"/> that the processor's
/// <c>ExecuteUpdate</c> never maintains. <c>CreatedAt</c> comes from the base — the same column it always was.
/// The setters are <c>internal</c>: this package writes its own rows through <see cref="MailSerializer"/>,
/// and nothing outside it should.
/// </remarks>
public sealed class QueuedMail : Entity<long>
{
    /// <summary>No form model: an email is enqueued by code, never posted.</summary>
    public const ModelWrites Writes = ModelWrites.None;

    /// <summary>The sender address, as JSON (see <see cref="MailSerializer"/>).</summary>
    public string From { get; internal set; } = "";

    /// <summary>The <c>To</c> recipients, as a JSON array.</summary>
    public string To { get; internal set; } = "";

    /// <summary>The <c>Cc</c> recipients, as a JSON array (or <c>null</c>).</summary>
    public string? Cc { get; internal set; }

    /// <summary>The <c>Bcc</c> recipients, as a JSON array (or <c>null</c>).</summary>
    public string? Bcc { get; internal set; }

    /// <summary>The <c>Reply-To</c> address, as JSON (or <c>null</c>).</summary>
    public string? ReplyTo { get; internal set; }

    /// <summary>The subject line.</summary>
    public string Subject { get; internal set; } = "";

    /// <summary>The HTML body, if any.</summary>
    public string? HtmlBody { get; internal set; }

    /// <summary>The <c>text/plain</c> body, if any.</summary>
    public string? TextBody { get; internal set; }

    /// <summary>The attachments, as a JSON array (or <c>null</c>).</summary>
    public string? Attachments { get; internal set; }

    /// <summary>The earliest time (UTC) the email is eligible to send — enqueue time, or later for a delayed send or a backed-off retry.</summary>
    public DateTime RunAt { get; internal set; }

    /// <summary>When the email was sent successfully (UTC), or <c>null</c> while it is pending.</summary>
    public DateTime? ProcessedAt { get; internal set; }

    /// <summary>How many times delivery has been attempted.</summary>
    public int Attempts { get; internal set; }

    /// <summary>The last failure message, if any.</summary>
    public string? Error { get; internal set; }

    /// <summary>
    /// The processor instance currently holding this email, or <c>null</c> when nobody does.
    /// </summary>
    /// <remarks>
    /// Also the optimistic-concurrency token, which is what stops an instance whose lease expired
    /// mid-send from stamping its outcome over the row another instance has since taken.
    /// </remarks>
    public Guid? ClaimToken { get; internal set; }

    /// <summary>
    /// When the current claim expires (UTC). Null or in the past means the email is claimable — which is
    /// also how a processor that died mid-send releases its work: the lease simply runs out.
    /// </summary>
    public DateTime? ClaimedUntil { get; internal set; }

    /// <summary>
    /// The user the email was queued for — <c>Current.UserId</c> at the time — or <c>null</c> when it was
    /// queued for nobody.
    /// </summary>
    /// <remarks>
    /// The processor re-enters it with <c>Current.UseUser</c> before calling the sender, exactly as a job does.
    /// </remarks>
    public Guid? UserId { get; internal set; }

    /// <summary>
    /// The tenant the email was queued for — <c>Current.Tenant</c> at the time — or <c>null</c> when it
    /// belongs to nobody.
    /// </summary>
    /// <remarks>
    /// Recorded as data, not as a partition: the processor sees every tenant's mail, and
    /// re-enters this one with <c>Tenant.Use</c> before calling the sender.
    /// </remarks>
    public Guid? TenantId { get; internal set; }
}
