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
