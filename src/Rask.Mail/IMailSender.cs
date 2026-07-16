namespace Rask.Mail;

/// <summary>
/// Delivers a fully-resolved <see cref="OutgoingMail"/> to its recipients. The
/// <see cref="MailProcessor{TContext}"/> resolves one from DI and calls it for each due message.
/// <c>AddRaskMail</c> selects a built-in implementation from <see cref="MailOptions"/> (SMTP via MailKit, an
/// <c>.eml</c> pickup directory, or logging), but you can register your own before calling it to override the
/// choice — for example to send through a provider API.
/// </summary>
public interface IMailSender
{
    /// <summary>Delivers <paramref name="mail"/>. Throw to signal a failure the processor should retry.</summary>
    Task SendAsync(OutgoingMail mail, CancellationToken cancellationToken = default);
}
