using Microsoft.Extensions.Logging;

namespace Rask.Mail;

/// <summary>
/// A no-transport <see cref="IMailSender"/> that logs each message instead of delivering it. The zero-config
/// fallback when neither <see cref="MailOptions.Smtp"/> nor <see cref="MailOptions.PickupDirectory"/> is set,
/// so <c>AddRaskMail</c> works in development without an SMTP server.
/// </summary>
public sealed partial class LogMailSender(ILogger<LogMailSender> logger) : IMailSender
{
    /// <inheritdoc/>
    public Task SendAsync(OutgoingMail mail, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mail);
        LogMail(logger, string.Join(", ", mail.To.Select(a => a.Address)), mail.Subject);
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Rask.Mail (no transport configured): would send email to {Recipients} — \"{Subject}\".")]
    private static partial void LogMail(ILogger logger, string recipients, string subject);
}
