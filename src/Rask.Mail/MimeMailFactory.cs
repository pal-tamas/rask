using MimeKit;

namespace Rask.Mail;

/// <summary>Builds a MimeKit <see cref="MimeMessage"/> from an <see cref="OutgoingMail"/> — shared by the SMTP and pickup-directory senders.</summary>
internal static class MimeMailFactory
{
    internal static MimeMessage Build(OutgoingMail mail)
    {
        var message = new MimeMessage();
        message.From.Add(ToMailbox(mail.From));
        AddAll(message.To, mail.To);
        AddAll(message.Cc, mail.Cc);
        AddAll(message.Bcc, mail.Bcc);
        if (mail.ReplyTo is { } replyTo)
        {
            message.ReplyTo.Add(ToMailbox(replyTo));
        }

        message.Subject = mail.Subject;

        var builder = new BodyBuilder();
        if (mail.HtmlBody is not null)
        {
            builder.HtmlBody = mail.HtmlBody;
        }

        if (mail.TextBody is not null)
        {
            builder.TextBody = mail.TextBody;
        }

        foreach (var attachment in mail.Attachments)
        {
            builder.Attachments.Add(attachment.FileName, attachment.Content, ContentType.Parse(attachment.ContentType));
        }

        message.Body = builder.ToMessageBody();
        return message;
    }

    private static void AddAll(InternetAddressList list, IReadOnlyList<EmailAddress> addresses)
    {
        foreach (var address in addresses)
        {
            list.Add(ToMailbox(address));
        }
    }

    private static MailboxAddress ToMailbox(EmailAddress address) => new(address.Name ?? "", address.Address);
}
