using Rask.Batteries;

namespace Rask.Mail;

/// <summary>A test's stand-in for the mail battery: <c>using var mail = Mail.Fake();</c>.</summary>
public static class MailFakes
{
    extension(Mail)
    {
        /// <summary>
        ///     Takes the place of the mail battery for this test, recording every send instead of queueing
        ///     it, until the returned fake is disposed:
        /// </summary>
        /// <remarks>
        ///     <code>
        ///     using var mail = Mail.Fake();
        ///
        ///     await page.Click("Create account");
        ///
        ///     mail.Sent().To("ann@x.io").Once();
        ///     </code>
        ///     <para>
        ///         Scoped to the test's own flow, so tests running in parallel never see each other's mail.
        ///         It stands in front of <c>Mail.Send</c>; a class that takes <see cref="IMail" /> in its
        ///         constructor is handed whatever the container holds, so register the fake there too —
        ///         <c>services.AddSingleton&lt;IMail&gt;(mail)</c> — when the code under test injects it.
        ///     </para>
        /// </remarks>
        public static MailFake Fake() => new();
    }
}

/// <summary>Every email a test sent, and the sentences that ask about them.</summary>
public sealed class MailFake : IMail, IDisposable
{
    private readonly List<SentEmail> _sent = [];
    private readonly IMail? _previous;
    private readonly Lock _gate = new();

    internal MailFake()
    {
        _previous = Mail.Faked.Value;
        Mail.Faked.Value = this;
    }

    /// <summary>
    ///     Asks about what was sent: <c>mail.Sent().To("ann@x.io").Once()</c>,
    ///     <c>mail.Sent().WithSubject("Welcome").Once()</c>, <c>mail.Sent().None()</c>. For anything the
    ///     steps do not cover, <c>Single()</c> hands back the one that matched.
    /// </summary>
    public Counting<SentEmail> Sent()
    {
        lock (_gate)
        {
            return new Counting<SentEmail>(
                [.. _sent], "email", "sent", static e => $"to \"{string.Join(", ", e.To)}\" — \"{e.Subject}\"");
        }
    }

    /// <summary>Forgets everything recorded, without putting the real battery back.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _sent.Clear();
        }
    }

    /// <summary>Puts the real mail battery back.</summary>
    public void Dispose() => Mail.Faked.Value = _previous;

    Task IMail.Add(Email email, DateTimeOffset? at, TimeSpan? after, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(email);
        var record = new SentEmail(
            [.. email.ToRecipients.Select(r => r.Address)],
            email.SubjectText ?? "",
            email.HtmlBody ?? "",
            after,
            at);

        lock (_gate)
        {
            _sent.Add(record);
        }

        return Task.CompletedTask;
    }
}

/// <summary>One email a test sent.</summary>
/// <param name="To">Its recipients' addresses.</param>
/// <param name="Subject">Its subject.</param>
/// <param name="Html">Its body, already rendered — <c>Body(component)</c> renders as the email is built.</param>
/// <param name="Delay">What <c>.In(…)</c> asked for, or <c>null</c>.</param>
/// <param name="Moment">What <c>.At(…)</c> asked for, or <c>null</c>.</param>
public sealed record SentEmail(
    IReadOnlyList<string> To,
    string Subject,
    string Html,
    TimeSpan? Delay,
    DateTimeOffset? Moment);

/// <summary>The steps that narrow what a test asks about its mail.</summary>
public static class SentEmailCounting
{
    extension(Counting<SentEmail> sent)
    {
        /// <summary>Only the mail addressed to <paramref name="address" />.</summary>
        public Counting<SentEmail> To(string address) =>
            sent.Where(e => e.To.Contains(address, StringComparer.OrdinalIgnoreCase), $"to \"{address}\"");

        /// <summary>Only the mail whose subject is <paramref name="subject" />.</summary>
        public Counting<SentEmail> WithSubject(string subject) =>
            sent.Where(e => string.Equals(e.Subject, subject, StringComparison.Ordinal), $"subject \"{subject}\"");

        /// <summary>Only the mail whose body contains <paramref name="text" />.</summary>
        public Counting<SentEmail> Saying(string text) =>
            sent.Where(e => e.Html.Contains(text, StringComparison.Ordinal), $"saying \"{text}\"");

        /// <summary>Only the mail held back by <c>.In(<paramref name="delay" />)</c>.</summary>
        public Counting<SentEmail> In(TimeSpan delay) =>
            sent.Where(e => e.Delay == delay, $"in {delay}");

        /// <summary>Only the mail held back until <c>.At(<paramref name="moment" />)</c>.</summary>
        public Counting<SentEmail> At(DateTimeOffset moment) =>
            sent.Where(e => e.Moment == moment, $"at {moment:u}");
    }
}
