namespace Rask.WebPush;

/// <summary>
///     How a send turned out, classified by what the caller should do about it.
/// </summary>
// The outcome of a send, classified so the caller knows what to do next.
public enum WebPushStatus
{
    /// <summary>Handed to the push service (2xx). Delivery to the device is the service's job from here —
    ///     this is not a receipt that the user saw anything.</summary>
    Success,

    /// <summary>The subscription no longer exists (404/410). Delete it from your store; retrying it will
    ///     never succeed.</summary>
    Expired,           // 404/410 — the subscription is gone; delete it from your store.

    /// <summary>Temporary — rate limited or the service is unwell (429/5xx). Retry later, honouring
    ///     <c>Retry-After</c> when it is present.</summary>
    TransientFailure,  // 429/5xx — retry later (honor Retry-After if present).

    /// <summary>Anything else (400/401/403/…), which usually means a VAPID or configuration mistake rather
    ///     than a bad subscription. Do not retry — fix the configuration.</summary>
    PermanentFailure   // everything else (400/401/403/…) — usually a VAPID/config error; don't retry.
}

/// <summary>
///     The result of a send. The flags map a <see cref="WebPushStatus" /> onto the action to take, so a
///     typical loop reads: <c>if (r.ShouldDelete) store.Remove(sub); else if (r.ShouldRetry) enqueue(sub);</c>
/// </summary>
/// <param name="Status">What happened.</param>
/// <param name="StatusCode">The HTTP status from the push service, when there was one.</param>
/// <param name="ReasonPhrase">The service's reason phrase, for logging. Do not show it to users.</param>
// The result of IWebPushSender.SendAsync. The convenience flags map a status to the action the
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
