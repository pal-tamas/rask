namespace Rask.WebPush;

// Configuration for the sender, supplied via AddRaskWebPush. The same VAPID key pair and contact
// subject are used for every message.
public sealed class WebPushOptions
{
    // The application-server key pair (see VapidKeys.Generate). Required.
    public VapidKeys? VapidKeys { get; set; }

    // The VAPID `sub` claim: a contact the push service can reach if your traffic causes problems.
    // Must be a "mailto:" address or an "https:" URL. Required.
    public string? Subject { get; set; }

    // Default retention used when a message's Ttl is zero. Sent as the "TTL" header.
    public TimeSpan DefaultTtl { get; set; } = TimeSpan.FromHours(12);

    // Throw if the options are unusable. Called once when the sender resolves.
    public void Validate()
    {
        if (VapidKeys is null || string.IsNullOrEmpty(VapidKeys.PublicKey) || string.IsNullOrEmpty(VapidKeys.PrivateKey))
            throw new InvalidOperationException(
                "WebPushOptions.VapidKeys must be set. Generate a pair once with VapidKeys.Generate() and store it.");

        if (string.IsNullOrEmpty(Subject))
            throw new InvalidOperationException(
                "WebPushOptions.Subject must be set to a 'mailto:' address or an 'https:' URL.");

        if (!Subject.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) &&
            !Subject.StartsWith("https:", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"WebPushOptions.Subject must start with 'mailto:' or 'https:' (was '{Subject}').");

        if (DefaultTtl < TimeSpan.Zero)
            throw new InvalidOperationException("WebPushOptions.DefaultTtl cannot be negative.");
    }
}
