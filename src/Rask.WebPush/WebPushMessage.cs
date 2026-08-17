namespace Rask.WebPush;

/// <summary>
///     How urgent a message is (RFC 8030 §5.3). The push service may delay or drop lower-urgency messages
///     to spare the device's battery and radio, so this is a real delivery lever, not a hint about how
///     loudly to notify.
/// </summary>
// Delivery urgency (RFC 8030 §5.3). The push service may delay or drop lower-urgency messages to
// save the device's battery/radio. Serialized to the wire token via WebPushSender.
public enum PushUrgency
{
    /// <summary>Advertisements and low-priority sync — safe to delay indefinitely.</summary>
    VeryLow, // "very-low" — advertisements, low-priority sync

    /// <summary>Chat and email: wanted soon, but not worth waking a sleeping device for.</summary>
    Low,     // "low"      — chat/email

    /// <summary>The default. Use it unless there is a reason not to.</summary>
    Normal,  // "normal"   — default

    /// <summary>
    ///     Time-sensitive — an incoming call, a security alert. Reserve it for messages that genuinely
    ///     cannot wait: marking everything high costs the user battery and earns nothing.
    /// </summary>
    High     // "high"     — incoming call, time-sensitive alert
}

/// <summary>
///     The notification to deliver. The typed fields serialize to the JSON shape the default service
///     worker already understands, so a push shows a notification with no service-worker changes.
/// </summary>
/// <remarks>
///     The payload is encrypted end-to-end (RFC 8291) — the push service relays it without being able to
///     read it. It is still delivered to a device that may be shared or shown on a lock screen, so keep
///     secrets out of the title and body and put the detail behind the click-through.
/// </remarks>
// The message to deliver. The typed fields are serialized to the JSON shape the default service
// worker (rask-sw.js) expects — { title, body, icon, badge, tag, data: { url } } — so a push shows
// a notification with no service-worker changes. Set RawPayload to send a hand-built JSON string
// instead (e.g. a richer payload handled by your own worker); it overrides the typed fields.
public sealed record WebPushMessage
{
    /// <summary>The notification's heading — the one line a user is certain to read.</summary>
    public string? Title { get; init; }

    /// <summary>The supporting line beneath the title.</summary>
    public string? Body { get; init; }

    /// <summary>URL of the icon shown beside the notification.</summary>
    public string? Icon { get; init; }

    /// <summary>URL of the small monochrome badge some platforms show in the status bar.</summary>
    public string? Badge { get; init; }

    /// <summary>
    ///     Groups notifications: a later one with the same tag REPLACES an earlier one still on screen
    ///     rather than stacking beside it. The fix for a counter that would otherwise post ten times.
    /// </summary>
    public string? Tag { get; init; }

    /// <summary>
    ///     Where to go when the notification is clicked. Send the user to the thing the notification is
    ///     about, not to the home page — the click is the whole point of the message.
    /// </summary>
    // The URL to focus/open when the notification is clicked. Serialized UNDER "data" (data.url) —
    // that is exactly where rask-sw.js's notificationclick handler reads it.
    public string? Url { get; init; }

    /// <summary>
    ///     A pre-built JSON payload, sent verbatim for your own service worker to interpret. When set,
    ///     every typed field above is ignored — so the default worker will not render it.
    /// </summary>
    // Escape hatch: a pre-built JSON payload sent verbatim. When set, the typed fields are ignored.
    public string? RawPayload { get; init; }

    /// <summary>
    ///     How long the push service should hold the message for a device that is offline. Zero — the
    ///     default — falls back to the sender's configured TTL. A short TTL on a time-sensitive message is
    ///     what stops it arriving hours late and confusing the user.
    /// </summary>
    // How long the push service should retain the message if the device is offline. Zero (the
    // default) falls back to WebPushOptions.DefaultTtl. Sent as the required "TTL" header.
    public TimeSpan Ttl { get; init; }

    /// <summary>How urgent this is. See <see cref="PushUrgency" />; defaults to
    ///     <see cref="PushUrgency.Normal" />.</summary>
    public PushUrgency Urgency { get; init; } = PushUrgency.Normal;

    /// <summary>
    ///     A collapse key (RFC 8030 §5.4): a later message with the same topic replaces an earlier one
    ///     that has not been delivered yet, so a device coming back online gets the latest rather than a
    ///     backlog. At most 32 base64url characters.
    /// </summary>
    // Optional collapse key (RFC 8030 §5.4): a later message with the same Topic replaces an
    // undelivered earlier one. Must be ≤ 32 base64url characters.
    public string? Topic { get; init; }

    /// <summary>A plain notification: a title, and optionally a body and a click-through URL.</summary>
    /// <param name="title">The heading.</param>
    /// <param name="body">The supporting line.</param>
    /// <param name="url">Where a click should go.</param>
    // A plain notification: title, optional body, optional click-through URL.
    public static WebPushMessage Text(string title, string? body = null, string? url = null) =>
        new() { Title = title, Body = body, Url = url };

    /// <summary>
    ///     A hand-built JSON payload, delivered verbatim for your own service worker to interpret.
    /// </summary>
    /// <param name="json">The payload. Your worker is responsible for rendering it — the default one
    ///     will not.</param>
    // Send a hand-built JSON payload verbatim (your own service worker interprets it).
    public static WebPushMessage Raw(string json) => new() { RawPayload = json };
}
