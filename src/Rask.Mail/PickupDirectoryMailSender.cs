namespace Rask.Mail;

/// <summary>
/// An <see cref="IMailSender"/> that writes each message to a directory as an <c>.eml</c> file instead of
/// contacting an SMTP server. Selected when <see cref="MailOptions.PickupDirectory"/> is set and no SMTP is
/// configured — useful for local development and tests (open the file in any mail client).
/// </summary>
public sealed class PickupDirectoryMailSender : IMailSender
{
    private readonly string _directory;

    /// <summary>Creates a sender that writes <c>.eml</c> files to <see cref="MailOptions.PickupDirectory"/>.</summary>
    public PickupDirectoryMailSender(MailOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _directory = options.PickupDirectory
            ?? throw new ArgumentException("PickupDirectoryMailSender requires MailOptions.PickupDirectory to be set.", nameof(options));
    }

    /// <inheritdoc/>
    public async Task SendAsync(OutgoingMail mail, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mail);
        Directory.CreateDirectory(_directory);
        var message = MimeMailFactory.Build(mail);

        var path = Path.Combine(_directory, $"{Guid.NewGuid():N}.eml");
        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await message.WriteToAsync(stream, cancellationToken).ConfigureAwait(false);
    }
}
