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
