using MimeKit;
using Rask.Core;

namespace Rask.Mail;

/// <summary>An email address with an optional display name.</summary>
/// <param name="Address">The email address (e.g. <c>jane@example.com</c>).</param>
/// <param name="Name">An optional display name (e.g. <c>Jane Doe</c>).</param>
public sealed record EmailAddress(string Address, string? Name = null);

/// <summary>A file attached to an email.</summary>
/// <param name="FileName">The attachment's file name (e.g. <c>invoice.pdf</c>).</param>
/// <param name="ContentType">The MIME content type (e.g. <c>application/pdf</c>).</param>
/// <param name="Content">The attachment bytes.</param>
public sealed record EmailAttachment(string FileName, string ContentType, byte[] Content);

/// <summary>
/// A fluent builder for an email. Start with <see cref="To(string, string?)"/>, chain recipients, a
/// <see cref="Subject"/>, and a <see cref="Body(Component)"/> (a Rask component rendered to HTML) or
/// <see cref="Body(string)"/> (raw HTML), then hand it to <see cref="IMailQueue.SendAsync"/>. After building
/// it holds only strings and bytes, so it serializes to a <see cref="QueuedMail"/> row trivially.
/// </summary>
public sealed class Email
{
    private readonly List<EmailAddress> _to = [];
    private readonly List<EmailAddress> _cc = [];
    private readonly List<EmailAddress> _bcc = [];
    private readonly List<EmailAttachment> _attachments = [];

    private Email() { }

    /// <summary>Begins a new email addressed to <paramref name="address"/>.</summary>
    public static Email To(string address, string? name = null)
    {
        var email = new Email();
        email._to.Add(MakeAddress(address, name));
        return email;
    }

    /// <summary>Adds another <c>To</c> recipient.</summary>
    public Email AndTo(string address, string? name = null)
    {
        _to.Add(MakeAddress(address, name));
        return this;
    }

    /// <summary>Adds a <c>Cc</c> recipient.</summary>
    public Email Cc(string address, string? name = null)
    {
        _cc.Add(MakeAddress(address, name));
        return this;
    }

    /// <summary>Adds a <c>Bcc</c> recipient.</summary>
    public Email Bcc(string address, string? name = null)
    {
        _bcc.Add(MakeAddress(address, name));
        return this;
    }

    /// <summary>Sets the <c>Reply-To</c> address.</summary>
    public Email ReplyTo(string address, string? name = null)
    {
        ReplyToAddress = MakeAddress(address, name);
        return this;
    }

    /// <summary>Overrides the sender for this message (otherwise <see cref="MailOptions.From"/> is used).</summary>
    public Email From(string address, string? name = null)
    {
        FromAddress = MakeAddress(address, name);
        return this;
    }

    // Validate the address up front with the same parser the sender uses, so a malformed address is rejected at
    // the call site rather than dead-lettering after MaxAttempts of backoff.
    private static EmailAddress MakeAddress(string address, string? name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(address);
        if (!MailboxAddress.TryParse(address, out _))
        {
            throw new ArgumentException($"'{address}' is not a valid email address.", nameof(address));
        }

        return new EmailAddress(address, name);
    }

    /// <summary>Sets the subject line.</summary>
    public Email Subject(string subject)
    {
        SubjectText = subject ?? throw new ArgumentNullException(nameof(subject));
        return this;
    }

    /// <summary>Sets the HTML body by rendering a Rask component to a standalone HTML string.</summary>
    public Email Body(Component component)
    {
        ArgumentNullException.ThrowIfNull(component);
        HtmlBody = component.ToHtml();
        return this;
    }

    /// <summary>Sets the HTML body from a raw HTML string.</summary>
    public Email Body(string html)
    {
        HtmlBody = html ?? throw new ArgumentNullException(nameof(html));
        return this;
    }

    /// <summary>Sets an optional <c>text/plain</c> alternative body.</summary>
    public Email PlainText(string text)
    {
        TextBody = text ?? throw new ArgumentNullException(nameof(text));
        return this;
    }

    /// <summary>Attaches a file.</summary>
    public Email Attach(string fileName, string contentType, byte[] content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);
        ArgumentNullException.ThrowIfNull(content);
        _attachments.Add(new EmailAttachment(fileName, contentType, content));
        return this;
    }

    internal IReadOnlyList<EmailAddress> ToRecipients => _to;
    internal IReadOnlyList<EmailAddress> CcRecipients => _cc;
    internal IReadOnlyList<EmailAddress> BccRecipients => _bcc;
    internal IReadOnlyList<EmailAttachment> Attachments => _attachments;
    internal EmailAddress? ReplyToAddress { get; private set; }
    internal EmailAddress? FromAddress { get; private set; }
    internal string? SubjectText { get; private set; }
    internal string? HtmlBody { get; private set; }
    internal string? TextBody { get; private set; }
}
