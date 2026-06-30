namespace Rask.WebPush;

// The outcome of a send, classified so the caller knows what to do next.
public enum WebPushStatus
{
    Success,           // 2xx — delivered to the push service.
    Expired,           // 404/410 — the subscription is gone; delete it from your store.
    TransientFailure,  // 429/5xx — retry later (honor Retry-After if present).
    PermanentFailure   // everything else (400/401/403/…) — usually a VAPID/config error; don't retry.
}

// The result of IWebPushSender.SendAsync. The convenience flags map a status to the action the
// caller should take, so a typical loop is: if (r.ShouldDelete) store.Remove(sub); else if
// (r.ShouldRetry) enqueue(sub).
public sealed record WebPushResult(WebPushStatus Status, int? StatusCode = null, string? ReasonPhrase = null)
{
    public bool IsSuccess => Status == WebPushStatus.Success;

    // True when the subscription no longer exists (HTTP 404/410) — remove it from your store.
    public bool ShouldDelete => Status == WebPushStatus.Expired;

    // True for a transient failure (HTTP 429/5xx) — the same message can be retried later.
    public bool ShouldRetry => Status == WebPushStatus.TransientFailure;
}
