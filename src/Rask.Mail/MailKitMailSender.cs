using MailKit.Net.Smtp;
using MailKit.Security;

namespace Rask.Mail;

/// <summary>
/// The default <see cref="IMailSender"/> when <see cref="MailOptions.Smtp"/> is configured: builds a MIME
/// message and sends it over SMTP with MailKit, opening and closing a fresh connection per message.
/// </summary>
public sealed class MailKitMailSender(MailOptions options) : IMailSender
{
    private readonly SmtpOptions _smtp = options.Smtp
        ?? throw new ArgumentException("MailKitMailSender requires MailOptions.Smtp to be set.", nameof(options));

    /// <inheritdoc/>
    public async Task SendAsync(OutgoingMail mail, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mail);
        var message = MimeMailFactory.Build(mail);

        using var client = new SmtpClient();
        await client.ConnectAsync(_smtp.Host, _smtp.Port, ToSocketOptions(_smtp.Security), cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(_smtp.User))
        {
            await client.AuthenticateAsync(_smtp.User, _smtp.Password ?? "", cancellationToken).ConfigureAwait(false);
        }

        await client.SendAsync(message, cancellationToken).ConfigureAwait(false);
        await client.DisconnectAsync(true, cancellationToken).ConfigureAwait(false);
    }

    private static SecureSocketOptions ToSocketOptions(SmtpSecurity security) => security switch
    {
        SmtpSecurity.StartTls => SecureSocketOptions.StartTls,
        SmtpSecurity.SslOnConnect => SecureSocketOptions.SslOnConnect,
        SmtpSecurity.None => SecureSocketOptions.None,
        _ => SecureSocketOptions.Auto,
    };
}
