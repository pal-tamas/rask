using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Rask.WebPush;

// Default IWebPush. Stateless apart from the validated options; one instance is shared and the
// HttpClient is supplied by IHttpClientFactory (see AddRaskWebPush).
/// <summary>
///     The default <see cref="IWebPush" />. Registered by <c>AddRaskWebPush</c> as a typed
///     <see cref="HttpClient" />, so inject the interface rather than constructing this.
/// </summary>
/// <remarks>
///     A VAPID token is valid for every request to the same push service until it expires, so signed
///     headers are cached per authority: broadcasting to a thousand subscribers on one service signs once,
///     not a thousand times.
/// </remarks>
public sealed partial class WebPushSender : IWebPush
{
    // A VAPID token is valid for any request to the same push-service authority until it expires, so
    // cache the signed Authorization header per authority instead of re-signing on every send — a
    // broadcast to N subscribers on one push service then signs once, not N times.
    private const int VapidLifetimeHours = 12;
    private const long VapidRefreshMarginSeconds = 300;

    private readonly ConcurrentDictionary<string, (string Header, long ExpiresAtUnix)> _vapidHeaders = new();
    private readonly HttpClient _http;
    private readonly WebPushOptions _options;
    private readonly ILogger<WebPushSender> _logger;

    /// <summary>Creates the sender. <c>AddRaskWebPush</c> does this for you.</summary>
    /// <param name="http">The HTTP client used to reach push services.</param>
    /// <param name="options">Validated options carrying the VAPID keys and contact subject.</param>
    /// <param name="logger">Optional. Failures log the endpoint and status — never the payload. Endpoints
    ///     are part of a subscription, so treat those logs accordingly.</param>
    public WebPushSender(HttpClient http, WebPushOptions options, ILogger<WebPushSender>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        _http = http;
        _options = options;
        _logger = logger ?? NullLogger<WebPushSender>.Instance;
    }

    /// <inheritdoc cref="IWebPush.SendAsync" />
    public async Task<WebPushResult> SendAsync(
        PushSubscription subscription,
        WebPushMessage message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        ArgumentNullException.ThrowIfNull(message);

        // Real Web Push endpoints are always absolute https URLs. Enforcing that rejects malformed
        // subscriptions and denies the obvious SSRF vectors (http:// to a metadata/loopback host) a
        // caller might otherwise relay an attacker-supplied subscription into.
        if (!Uri.TryCreate(subscription.Endpoint, UriKind.Absolute, out Uri? endpoint) ||
            endpoint.Scheme != Uri.UriSchemeHttps)
            throw new ArgumentException("Push subscription endpoint must be an absolute https URL.", nameof(subscription));

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);

        string? payload = BuildPayload(message);
        if (payload is null)
        {
            // A "tickle" — no payload, no encryption, just a wake-up. The body must be empty.
            request.Content = new ByteArrayContent([]);
        }
        else
        {
            byte[] body = Aes128GcmEncryptor.Encrypt(
                Base64Url.Decode(subscription.P256dh),
                Base64Url.Decode(subscription.Auth),
                Encoding.UTF8.GetBytes(payload));

            var content = new ByteArrayContent(body);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            content.Headers.ContentEncoding.Add("aes128gcm");
            request.Content = content;
        }

        var ttl = message.Ttl > TimeSpan.Zero ? message.Ttl : _options.DefaultTtl;
        request.Headers.TryAddWithoutValidation("TTL", ((int)ttl.TotalSeconds).ToString(CultureInfo.InvariantCulture));
        request.Headers.TryAddWithoutValidation("Urgency", UrgencyToken(message.Urgency));
        if (!string.IsNullOrEmpty(message.Topic))
            request.Headers.TryAddWithoutValidation("Topic", message.Topic);
        request.Headers.TryAddWithoutValidation("Authorization", VapidAuthorization(endpoint));

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            // Couldn't reach the push service (DNS/connection/TLS) — transient by nature; let the
            // caller retry. A caller-requested cancellation is not caught and propagates.
            _logger.LogWarning(ex, "Web Push send to {Endpoint} could not reach the push service.", subscription.Endpoint);
            return new WebPushResult(WebPushStatus.TransientFailure, null, ex.Message);
        }

        using (response)
        {
            int code = (int)response.StatusCode;
            WebPushStatus status = code switch
            {
                >= 200 and < 300 => WebPushStatus.Success,
                404 or 410 => WebPushStatus.Expired,
                429 or >= 500 and < 600 => WebPushStatus.TransientFailure,
                _ => WebPushStatus.PermanentFailure
            };

            if (status is WebPushStatus.PermanentFailure)
                _logger.LogWarning("Web Push send to {Endpoint} failed permanently: {Code} {Reason}",
                    subscription.Endpoint, code, response.ReasonPhrase);

            return new WebPushResult(status, code, response.ReasonPhrase);
        }
    }

    // The cached "vapid t=…,k=…" header for the endpoint's push-service authority, signing a fresh JWT
    // only on a cache miss or when the cached one is within the refresh margin of expiring.
    private string VapidAuthorization(Uri endpoint)
    {
        string audience = endpoint.GetLeftPart(UriPartial.Authority);
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (_vapidHeaders.TryGetValue(audience, out var cached) && cached.ExpiresAtUnix - now > VapidRefreshMarginSeconds)
            return cached.Header;

        var expires = DateTimeOffset.UtcNow.AddHours(VapidLifetimeHours);
        string header = Vapid.BuildAuthorizationHeader(endpoint.ToString(), _options.VapidKeys!, _options.Subject!, expires);
        _vapidHeaders[audience] = (header, expires.ToUnixTimeSeconds());
        return header;
    }

    // Maps the typed message to the JSON shape rask-sw.js reads, or null for a payload-less tickle.
    // RawPayload wins when set.
    private static string? BuildPayload(WebPushMessage message)
    {
        if (message.RawPayload is not null)
            return message.RawPayload;

        if (message is { Title: null, Body: null, Icon: null, Badge: null, Tag: null, Url: null })
            return null;

        var dto = new PushPayload
        {
            Title = message.Title,
            Body = message.Body,
            Icon = message.Icon,
            Badge = message.Badge,
            Tag = message.Tag,
            Data = message.Url is null ? null : new PushPayloadData { Url = message.Url }
        };
        return JsonSerializer.Serialize(dto, PushPayloadJsonContext.Default.PushPayload);
    }

    private static string UrgencyToken(PushUrgency urgency) => urgency switch
    {
        PushUrgency.VeryLow => "very-low",
        PushUrgency.Low => "low",
        PushUrgency.High => "high",
        _ => "normal"
    };

    // Mirrors the { title, body, icon, badge, tag, data: { url } } contract in rask-sw.js.
    private sealed class PushPayload
    {
        [JsonPropertyName("title")] public string? Title { get; init; }
        [JsonPropertyName("body")] public string? Body { get; init; }
        [JsonPropertyName("icon")] public string? Icon { get; init; }
        [JsonPropertyName("badge")] public string? Badge { get; init; }
        [JsonPropertyName("tag")] public string? Tag { get; init; }
        [JsonPropertyName("data")] public PushPayloadData? Data { get; init; }
    }

    private sealed class PushPayloadData
    {
        [JsonPropertyName("url")] public string? Url { get; init; }
    }

    [JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonSerializable(typeof(PushPayload))]
    private sealed partial class PushPayloadJsonContext : JsonSerializerContext;
}
