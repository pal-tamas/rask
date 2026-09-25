namespace Rask.WebPush;

/// <summary>
///     The result of a send. The flags map a <see cref="WebPushStatus" /> onto the action to take, so a
///     typical loop reads: <c>if (r.ShouldDelete) store.Remove(sub); else if (r.ShouldRetry) enqueue(sub);</c>
/// </summary>
/// <param name="Status">What happened.</param>
/// <param name="StatusCode">The HTTP status from the push service, when there was one.</param>
/// <param name="ReasonPhrase">The service's reason phrase, for logging. Do not show it to users.</param>
// The result of IWebPush.Send. The convenience flags map a status to the action the
// caller should take, so a typical loop is: if (r.ShouldDelete) store.Remove(sub); else if
// (r.ShouldRetry) enqueue(sub).
public sealed record WebPushResult(WebPushStatus Status, int? StatusCode = null, string? ReasonPhrase = null)
{
    /// <summary>The push service accepted the message.</summary>
    public bool IsSuccess => Status == WebPushStatus.Success;

    /// <summary>
    ///     The subscription is gone (404/410) — remove it from your store. Leaving dead subscriptions in
    ///     place means every later broadcast pays for them.
    /// </summary>
    // True when the subscription no longer exists (HTTP 404/410) — remove it from your store.
    public bool ShouldDelete => Status == WebPushStatus.Expired;

    /// <summary>
    ///     A transient failure (429/5xx) — the same message can be sent again later. Back off rather than
    ///     retrying immediately; the service is already telling you it is overloaded.
    /// </summary>
    // True for a transient failure (HTTP 429/5xx) — the same message can be retried later.
    public bool ShouldRetry => Status == WebPushStatus.TransientFailure;
}
