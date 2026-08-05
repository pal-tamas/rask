namespace Rask.Mail;

/// <summary>Transport security for the SMTP connection.</summary>
public enum SmtpSecurity
{
    /// <summary>Let MailKit choose based on the port (STARTTLS on 587, implicit TLS on 465). The default.</summary>
    Auto,

    /// <summary>Connect in the clear then upgrade with STARTTLS (typically port 587).</summary>
    StartTls,

    /// <summary>Implicit TLS from connect (typically port 465).</summary>
    SslOnConnect,

    /// <summary>No transport encryption (development / a local relay only).</summary>
    None,
}

/// <summary>SMTP server connection settings.</summary>
public sealed class SmtpOptions
{
    /// <summary>The SMTP server host name.</summary>
    public string Host { get; set; } = "";

    /// <summary>The SMTP server port. Default 587 (submission).</summary>
    public int Port { get; set; } = 587;

    /// <summary>The user name for SMTP authentication, or <c>null</c> to connect unauthenticated.</summary>
    public string? User { get; set; }

    /// <summary>The password for SMTP authentication.</summary>
    public string? Password { get; set; }

    /// <summary>The transport security mode. Default <see cref="SmtpSecurity.Auto"/>.</summary>
    public SmtpSecurity Security { get; set; } = SmtpSecurity.Auto;
}

/// <summary>Options for <see cref="MailProcessor{TContext}"/> and the sender it uses.</summary>
public sealed class MailOptions
{
    /// <summary>The default sender address, used when an <see cref="Email"/> doesn't set its own. Required.</summary>
    public string From { get; set; } = "";

    /// <summary>An optional display name for the default sender.</summary>
    public string? FromName { get; set; }

    /// <summary>
    /// SMTP settings. When set, mail is delivered over SMTP (MailKit). When <c>null</c>, delivery falls back to
    /// <see cref="PickupDirectory"/> (if set) or to logging — so the pillar works with zero configuration in
    /// development.
    /// </summary>
    public SmtpOptions? Smtp { get; set; }

    /// <summary>
    /// A directory to write sent messages to as <c>.eml</c> files instead of contacting an SMTP server. Used
    /// when <see cref="Smtp"/> is not set — handy for local development and tests.
    /// </summary>
    public string? PickupDirectory { get; set; }

    /// <summary>How often the processor polls the mail table for due messages. Default 5s.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>How many messages to send per poll. Default 100.</summary>
    public int BatchSize { get; set; } = 100;

    /// <summary>How many times to attempt a failing message before it is left as a dead letter (kept for inspection). Default 10.</summary>
    public int MaxAttempts { get; set; } = 10;

    /// <summary>The base delay before the first retry; each further retry doubles it (capped at <see cref="MaxRetryDelay"/>). Default 30s.</summary>
    public TimeSpan BaseRetryDelay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>The cap on the exponential retry backoff. Default 1h.</summary>
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromHours(1);

    /// <summary>How long sent messages are kept before being purged. <see cref="TimeSpan.Zero"/> keeps them forever. Default 7 days.</summary>
    public TimeSpan RetentionPeriod { get; set; } = TimeSpan.FromDays(7);

    /// <summary>
    /// How long a send that is already in flight may keep going after the host is asked to stop.
    /// <para>
    /// On <c>SIGTERM</c> the processor immediately stops picking up <em>new</em> messages, but the send
    /// already talking to your SMTP server is given this long to finish rather than being cancelled
    /// mid-conversation.
    /// </para>
    /// <para>
    /// <b>This is the one battery where the grace period buys more than tidiness.</b> Delivery and the row
    /// update are not one transaction, so a send cancelled during the SMTP <c>DATA</c> phase may already
    /// have been accepted and queued by the server while the row still reads unsent — and the next boot
    /// re-sends it. Mail is at-least-once and cannot be made otherwise from here; the grace period is what
    /// makes that window rare rather than routine. Default 10s — double the other batteries, because an
    /// interrupted send is a possible <em>duplicate</em>, not a clean retry.
    /// </para>
    /// <para>
    /// Cannot exceed <c>HostOptions.ShutdownTimeout</c>: once that elapses the host stops waiting for
    /// hosted services, so a grace longer than it silently does not happen.
    /// <see cref="TimeSpan.Zero"/> cancels immediately.
    /// </para>
    /// </summary>
    public TimeSpan ShutdownGracePeriod { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Validates the option values (called at registration, so a bad value fails fast rather than tearing down the host later).</summary>
    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(From))
        {
            throw new ArgumentException("MailOptions.From is required — set a default sender address.", nameof(From));
        }

        if (Smtp is not null && string.IsNullOrWhiteSpace(Smtp.Host))
        {
            throw new ArgumentException("MailOptions.Smtp.Host is required when SMTP is configured.", nameof(Smtp));
        }

        if (PollInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(PollInterval), PollInterval, "PollInterval must be positive.");
        }

        if (BatchSize < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(BatchSize), BatchSize, "BatchSize must be at least 1.");
        }

        if (MaxAttempts < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxAttempts), MaxAttempts, "MaxAttempts must be at least 1.");
        }

        if (BaseRetryDelay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(BaseRetryDelay), BaseRetryDelay, "BaseRetryDelay cannot be negative.");
        }

        if (MaxRetryDelay < BaseRetryDelay)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxRetryDelay), MaxRetryDelay, "MaxRetryDelay cannot be less than BaseRetryDelay.");
        }

        if (RetentionPeriod < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(RetentionPeriod), RetentionPeriod, "RetentionPeriod cannot be negative.");
        }

        if (ShutdownGracePeriod < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ShutdownGracePeriod), ShutdownGracePeriod, "ShutdownGracePeriod cannot be negative (Zero cancels immediately).");
        }

        // CancellationTokenSource.CancelAfter throws above int.MaxValue milliseconds, and it would throw
        // from the shutdown path — the worst place to find out.
        if (ShutdownGracePeriod.TotalMilliseconds > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ShutdownGracePeriod), ShutdownGracePeriod, $"ShutdownGracePeriod must be at most {TimeSpan.FromMilliseconds(int.MaxValue)}.");
        }
    }

    /// <summary>
    /// The delay before the next retry of a message on its <paramref name="attempts"/>-th attempt: an
    /// exponential backoff (<see cref="BaseRetryDelay"/> × 2^(attempts-1)) capped at <see cref="MaxRetryDelay"/>.
    /// Pure and deterministic.
    /// </summary>
    internal TimeSpan RetryDelay(int attempts)
    {
        if (attempts <= 1)
        {
            return BaseRetryDelay;
        }

        var scaled = BaseRetryDelay.Ticks * Math.Pow(2, attempts - 1);
        return double.IsInfinity(scaled) || scaled >= MaxRetryDelay.Ticks
            ? MaxRetryDelay
            : TimeSpan.FromTicks((long)scaled);
    }
}
