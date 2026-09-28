using Microsoft.Extensions.Logging;

namespace Rask.Mailing;

/// <summary>
/// A no-transport <see cref="IMailSender"/> that logs each message instead of delivering it. The zero-config
/// fallback when neither <see cref="MailOptions.Smtp"/> nor <see cref="MailOptions.PickupDirectory"/> is set,
/// so <c>AddRaskMail</c> works in development without an SMTP server.
/// </summary>
public sealed partial class LogMailSender(ILogger<LogMailSender> logger) : IMailSender
{
    /// <inheritdoc/>
    public Task Send(OutgoingMail mail, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mail);
        if (logger.IsEnabled(LogLevel.Information))
        {
#pragma warning disable CA1873 // guarded by the IsEnabled check above, which CA1873 does not see through a [LoggerMessage] call
            LogMail(logger, string.Join(", ", mail.To.Select(a => a.Address)), mail.Subject);
#pragma warning restore CA1873
        }

        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Rask.Mail (no transport configured): would send email to {Recipients} — \"{Subject}\".")]
    private static partial void LogMail(ILogger logger, string recipients, string subject);
}
