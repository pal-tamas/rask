using System.Text.Json;

namespace Rask.Mail;

/// <summary>
/// Converts between the fluent <see cref="Email"/> / send-ready <see cref="OutgoingMail"/> and the persisted
/// <see cref="QueuedMail"/> row. Recipient lists and attachments are stored as JSON (attachment bytes as
/// base64, the default for <see cref="byte"/>[]). Bodies are already HTML by the time they reach here.
/// </summary>
internal static class MailSerializer
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    /// <summary>Builds a persisted row from a built <see cref="Email"/>, defaulting the sender and stamping times.</summary>
    internal static QueuedMail ToQueuedMail(Email email, EmailAddress from, DateTime runAt, DateTime createdAt)
    {
        if (email.ToRecipients.Count == 0)
        {
            throw new ArgumentException("An email must have at least one 'To' recipient.", nameof(email));
        }

        if (email.HtmlBody is null && email.TextBody is null)
        {
            throw new ArgumentException("An email must have a body — call Body(...) or PlainText(...).", nameof(email));
        }

        return new QueuedMail
        {
            From = SerializeAddress(from),
            To = JsonSerializer.Serialize(email.ToRecipients, Options),
            Cc = ToJsonOrNull(email.CcRecipients),
            Bcc = ToJsonOrNull(email.BccRecipients),
            ReplyTo = email.ReplyToAddress is { } r ? SerializeAddress(r) : null,
            Subject = email.SubjectText ?? "",
            HtmlBody = email.HtmlBody,
            TextBody = email.TextBody,
            Attachments = ToJsonOrNull(email.Attachments),
            RunAt = runAt,
            CreatedAt = createdAt,
        };
    }

    /// <summary>Materializes a send-ready <see cref="OutgoingMail"/> from a persisted row.</summary>
    internal static OutgoingMail ToOutgoing(QueuedMail message) => new()
    {
        From = DeserializeAddress(message.From),
        To = DeserializeAddresses(message.To),
        Cc = DeserializeAddresses(message.Cc),
        Bcc = DeserializeAddresses(message.Bcc),
        ReplyTo = message.ReplyTo is { } r ? DeserializeAddress(r) : null,
        Subject = message.Subject,
        HtmlBody = message.HtmlBody,
        TextBody = message.TextBody,
        Attachments = message.Attachments is { } a
            ? JsonSerializer.Deserialize<List<EmailAttachment>>(a, Options) ?? []
            : [],
    };

    private static string SerializeAddress(EmailAddress address) => JsonSerializer.Serialize(address, Options);

    private static EmailAddress DeserializeAddress(string json) =>
        JsonSerializer.Deserialize<EmailAddress>(json, Options)
        ?? throw new InvalidOperationException("A mail row has a malformed address.");

    private static IReadOnlyList<EmailAddress> DeserializeAddresses(string? json) =>
        string.IsNullOrEmpty(json) ? [] : JsonSerializer.Deserialize<List<EmailAddress>>(json, Options) ?? [];

    private static string? ToJsonOrNull<T>(IReadOnlyList<T> items) =>
        items.Count == 0 ? null : JsonSerializer.Serialize(items, Options);
}
