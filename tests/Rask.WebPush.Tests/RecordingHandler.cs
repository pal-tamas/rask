using System.Net;

namespace Rask.WebPush.Tests;

// Captures the request WebPushSender builds and returns a canned status, so tests can assert headers
// and body offline and exercise the status→result mapping.
internal sealed class RecordingHandler(HttpStatusCode status = HttpStatusCode.Created) : HttpMessageHandler
{
    public HttpRequestMessage? Request { get; private set; }
    public byte[] Body { get; private set; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Request = request;
        Body = request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync(cancellationToken);
        return new HttpResponseMessage(status) { ReasonPhrase = status.ToString() };
    }
}

internal static class TestSender
{
    public const string Endpoint = "https://fcm.googleapis.com/fcm/send/abc123";

    public static WebPushOptions Options() => new()
    {
        VapidKeys = VapidKeys.Generate(),
        Subject = "mailto:admin@example.com"
    };

    public static WebPushSender Create(RecordingHandler handler, WebPushOptions? options = null) =>
        new(new HttpClient(handler), options ?? Options());
}
