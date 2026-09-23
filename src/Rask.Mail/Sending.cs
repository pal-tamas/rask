using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Rask.Mail;

/// <summary>
///     A <c>Send</c> still being worded: <c>await Mail.Send(reminder).In(24.Hours)</c>. Nothing is queued
///     until it is awaited.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public readonly struct Sending
{
    private readonly IMail? _mail;
    private readonly Email _email;
    private readonly DateTimeOffset? _at;
    private readonly TimeSpan? _after;
    private readonly CancellationToken _cancellationToken;

    internal Sending(IMail? mail, Email email, DateTimeOffset? at, TimeSpan? after, CancellationToken cancellationToken)
    {
        _mail = mail;
        _email = email ?? throw new ArgumentNullException(nameof(email));
        _at = at;
        _after = after;
        _cancellationToken = cancellationToken;
    }

    /// <summary>Sends it no earlier than <paramref name="delay"/> from now: <c>.In(24.Hours)</c>.</summary>
    public Sending In(TimeSpan delay)
    {
        if (delay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(delay), delay, "In cannot be negative — an email cannot be sent in the past.");
        }

        return new Sending(_mail, _email, null, delay, _cancellationToken);
    }

    /// <summary>Sends it no earlier than <paramref name="moment"/>: <c>.At(tomorrowMorning)</c>.</summary>
    public Sending At(DateTimeOffset moment) => new(_mail, _email, moment, null, _cancellationToken);

    /// <summary>Runs it.</summary>
    public TaskAwaiter GetAwaiter() => AsTask().GetAwaiter();

    /// <summary>Runs it, choosing whether the continuation returns to the captured context.</summary>
    public ConfiguredTaskAwaitable ConfigureAwait(bool continueOnCapturedContext) =>
        AsTask().ConfigureAwait(continueOnCapturedContext);

    /// <summary>Runs it, as a <see cref="Task"/>.</summary>
    public Task AsTask() => (_mail ?? Mail.Resolve()).Add(_email, _at, _after, Ambient.Or(_cancellationToken));
}
