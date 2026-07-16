namespace Rask.Mail;

/// <summary>
/// A fully-resolved email ready to send, materialized from a stored <see cref="QueuedMail"/> and handed to
/// an <see cref="IMailSender"/>. The sender (envelope) address is always set — the queue defaults it from
/// <see cref="MailOptions.From"/> when the message didn't override it.
/// </summary>
public sealed class OutgoingMail
{
    /// <summary>The sender address.</summary>
    public required EmailAddress From { get; init; }

    /// <summary>The <c>To</c> recipients (at least one).</summary>
    public required IReadOnlyList<EmailAddress> To { get; init; }

    /// <summary>The <c>Cc</c> recipients.</summary>
    public IReadOnlyList<EmailAddress> Cc { get; init; } = [];

    /// <summary>The <c>Bcc</c> recipients.</summary>
    public IReadOnlyList<EmailAddress> Bcc { get; init; } = [];

    /// <summary>The optional <c>Reply-To</c> address.</summary>
    public EmailAddress? ReplyTo { get; init; }

    /// <summary>The subject line.</summary>
    public required string Subject { get; init; }

    /// <summary>The HTML body, if any.</summary>
    public string? HtmlBody { get; init; }

    /// <summary>The <c>text/plain</c> body, if any.</summary>
    public string? TextBody { get; init; }

    /// <summary>The file attachments.</summary>
    public IReadOnlyList<EmailAttachment> Attachments { get; init; } = [];
}
