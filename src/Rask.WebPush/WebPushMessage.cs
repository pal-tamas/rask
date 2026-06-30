namespace Rask.WebPush;

// Delivery urgency (RFC 8030 §5.3). The push service may delay or drop lower-urgency messages to
// save the device's battery/radio. Serialized to the wire token via WebPushSender.
public enum PushUrgency
{
    VeryLow, // "very-low" — advertisements, low-priority sync
    Low,     // "low"      — chat/email
    Normal,  // "normal"   — default
    High     // "high"     — incoming call, time-sensitive alert
}

// The message to deliver. The typed fields are serialized to the JSON shape the default service
// worker (rask-sw.js) expects — { title, body, icon, badge, tag, data: { url } } — so a push shows
// a notification with no service-worker changes. Set RawPayload to send a hand-built JSON string
// instead (e.g. a richer payload handled by your own worker); it overrides the typed fields.
public sealed record WebPushMessage
{
    public string? Title { get; init; }
    public string? Body { get; init; }
    public string? Icon { get; init; }
    public string? Badge { get; init; }
    public string? Tag { get; init; }

    // The URL to focus/open when the notification is clicked. Serialized UNDER "data" (data.url) —
    // that is exactly where rask-sw.js's notificationclick handler reads it.
    public string? Url { get; init; }

    // Escape hatch: a pre-built JSON payload sent verbatim. When set, the typed fields are ignored.
    public string? RawPayload { get; init; }

    // How long the push service should retain the message if the device is offline. Zero (the
    // default) falls back to WebPushOptions.DefaultTtl. Sent as the required "TTL" header.
    public TimeSpan Ttl { get; init; }

    public PushUrgency Urgency { get; init; } = PushUrgency.Normal;

    // Optional collapse key (RFC 8030 §5.4): a later message with the same Topic replaces an
    // undelivered earlier one. Must be ≤ 32 base64url characters.
    public string? Topic { get; init; }

    // A plain notification: title, optional body, optional click-through URL.
    public static WebPushMessage Text(string title, string? body = null, string? url = null) =>
        new() { Title = title, Body = body, Url = url };

    // Send a hand-built JSON payload verbatim (your own service worker interprets it).
    public static WebPushMessage Raw(string json) => new() { RawPayload = json };
}
