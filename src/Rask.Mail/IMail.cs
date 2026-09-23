using System.ComponentModel;

namespace Rask.Mail;

/// <summary>
/// The app's outgoing mail. Reach it without injecting anything — <c>await Mail.Send(email)</c> — or inject
/// this and word the same sentence: <c>await mail.Send(email).In(24.Hours)</c>.
/// </summary>
public interface IMail
{
    /// <summary>
    /// Queues one email, due at <paramref name="at"/>, or <paramref name="after"/> from now, or now when
    /// neither is given. The primitive under <c>Send</c>; call <c>Send(email)</c> and its <c>In</c>/<c>At</c>
    /// steps instead.
    /// </summary>
    /// <remarks>
    /// <paramref name="after"/> arrives as a duration rather than an instant because "now" is the queue's to
    /// decide: it holds the app's <see cref="TimeProvider"/>, which is what a test moves, and a caller that
    /// resolved the delay itself would queue against a clock the processor never reads.
    /// </remarks>
    [EditorBrowsable(EditorBrowsableState.Never)]
    Task Add(Email email, DateTimeOffset? at, TimeSpan? after, CancellationToken cancellationToken = default);
}
