using Rask.Batteries;

namespace Rask.Mailing;

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
