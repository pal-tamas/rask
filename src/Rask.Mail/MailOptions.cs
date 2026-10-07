namespace Rask.Mailing;

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

    /// <summary>The ceiling on <see cref="BatchSize"/> — see <see cref="MailOptionsValidator"/> for why there is one.</summary>
    internal const int MaxBatchSize = 1000;

    /// <summary>How often the processor polls the mail table for due messages. Default 5s.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>How many messages to send per poll. Default 100.</summary>
    public int BatchSize { get; set; } = 100;

    /// <summary>
    /// How long a claimed email stays invisible to other processor instances. Default 5 minutes.
    /// </summary>
    /// <remarks>
    /// This is the recovery window, not a timeout: nothing cancels a send that overruns it. A processor
    /// that dies mid-send makes its work claimable again after this long, so it must comfortably exceed the
    /// slowest SMTP handshake you see — set it too low and a slow send is picked up by a second instance
    /// while the first is still waiting on the server, and the recipient gets the email twice.
    /// </remarks>
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>How many times to attempt a failing message before it is left as a dead letter (kept for inspection). Default 10.</summary>
    public int MaxAttempts { get; set; } = 10;

    /// <summary>The base delay before the first retry; each further retry doubles it (capped at <see cref="MaxRetryDelay"/>). Default 30s.</summary>
    public TimeSpan BaseRetryDelay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>The cap on the exponential retry backoff. Default 1h.</summary>
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromHours(1);

    /// <summary>How long sent messages are kept before being purged. <see cref="TimeSpan.Zero"/> keeps them forever. Default 7 days.</summary>
    public TimeSpan Retention { get; set; } = TimeSpan.FromDays(7);

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
