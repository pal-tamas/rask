namespace Rask.WebPush;

// Configuration for the sender, supplied via AddRaskWebPush. The same VAPID key pair and contact
// subject are used for every message.
/// <summary>
///     Configures the push sender, through <c>AddRaskWebPush</c>. One VAPID pair and one contact address
///     serve every message the app sends.
/// </summary>
public sealed class WebPushOptions
{
    /// <summary>
    ///     The application-server key pair — required. Generate it once with
    ///     <see cref="Rask.WebPush.VapidKeys.Generate" /> and load it from configuration or secrets;
    ///     generating a fresh pair at startup would unsubscribe every user on every deploy.
    /// </summary>
    public VapidKeys? VapidKeys { get; set; }

    /// <summary>
    ///     How a push service reaches you if your traffic causes it problems — required, and it must be a
    ///     <c>mailto:</c> address or an <c>https:</c> URL. Point it at something a person actually reads:
    ///     the alternative to being contacted is being blocked.
    /// </summary>
    public string? Subject { get; set; }

    /// <summary>
    ///     How long a push service holds a message for an offline device when the message itself sets no
    ///     TTL. Defaults to 12 hours.
    /// </summary>
    public TimeSpan DefaultTtl { get; set; } = TimeSpan.FromHours(12);

    /// <summary>
    ///     Throws if these options cannot send. Called once when the sender is resolved, so a
    ///     misconfiguration fails at startup rather than on the first notification nobody receives.
    /// </summary>
    /// <exception cref="InvalidOperationException">The keys are missing, the subject is missing or is
    ///     neither <c>mailto:</c> nor <c>https:</c>, or the TTL is negative.</exception>
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
