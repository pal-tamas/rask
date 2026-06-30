using System.Net;

namespace Rask.WebPush.Tests;

// Asserts the request shape (method/URL/headers) and the HTTP-status → WebPushResult mapping.
public sealed class WebPushSenderTests
{
    private static PushSubscription Sub(TestCrypto.Client c) => new(TestSender.Endpoint, c.P256dhB64, c.AuthB64);

    [Fact]
    public async Task Posts_encrypted_body_with_required_headers()
    {
        var handler = new RecordingHandler();
        using var client = TestCrypto.GenerateClient();

        await TestSender.Create(handler).SendAsync(Sub(client),
            new WebPushMessage { Title = "T", Urgency = PushUrgency.High, Topic = "news" });

        HttpRequestMessage req = handler.Request!;
        Assert.Equal(HttpMethod.Post, req.Method);
        Assert.Equal(TestSender.Endpoint, req.RequestUri!.ToString());
        Assert.Equal("aes128gcm", req.Content!.Headers.ContentEncoding.Single());
        Assert.Equal("application/octet-stream", req.Content.Headers.ContentType!.MediaType);
        Assert.True(req.Headers.Contains("TTL"));
        Assert.Equal("43200", req.Headers.GetValues("TTL").Single()); // default 12h.
        Assert.Equal("high", req.Headers.GetValues("Urgency").Single());
        Assert.Equal("news", req.Headers.GetValues("Topic").Single());
        Assert.StartsWith("vapid ", req.Headers.GetValues("Authorization").Single(), StringComparison.Ordinal);
        Assert.NotEmpty(handler.Body);
    }

    [Fact]
    public async Task Custom_ttl_overrides_the_default()
    {
        var handler = new RecordingHandler();
        using var client = TestCrypto.GenerateClient();

        await TestSender.Create(handler).SendAsync(Sub(client),
            new WebPushMessage { Title = "T", Ttl = TimeSpan.FromMinutes(5) });

        Assert.Equal("300", handler.Request!.Headers.GetValues("TTL").Single());
    }

    [Fact]
    public async Task Topic_omitted_when_unset()
    {
        var handler = new RecordingHandler();
        using var client = TestCrypto.GenerateClient();

        await TestSender.Create(handler).SendAsync(Sub(client), WebPushMessage.Text("T"));

        Assert.False(handler.Request!.Headers.Contains("Topic"));
    }

    [Fact]
    public async Task Empty_message_sends_a_payloadless_tickle()
    {
        var handler = new RecordingHandler();
        using var client = TestCrypto.GenerateClient();

        await TestSender.Create(handler).SendAsync(Sub(client), new WebPushMessage());

        Assert.Empty(handler.Body);
        Assert.Empty(handler.Request!.Content!.Headers.ContentEncoding); // no aes128gcm for a tickle.
    }

    [Theory]
    [InlineData(HttpStatusCode.Created, WebPushStatus.Success)]
    [InlineData(HttpStatusCode.OK, WebPushStatus.Success)]
    [InlineData(HttpStatusCode.Gone, WebPushStatus.Expired)]
    [InlineData(HttpStatusCode.NotFound, WebPushStatus.Expired)]
    [InlineData(HttpStatusCode.TooManyRequests, WebPushStatus.TransientFailure)]
    [InlineData(HttpStatusCode.ServiceUnavailable, WebPushStatus.TransientFailure)]
    [InlineData(HttpStatusCode.BadRequest, WebPushStatus.PermanentFailure)]
    [InlineData(HttpStatusCode.Unauthorized, WebPushStatus.PermanentFailure)]
    public async Task Maps_http_status_to_result(HttpStatusCode http, WebPushStatus expected)
    {
        var handler = new RecordingHandler(http);
        using var client = TestCrypto.GenerateClient();

        WebPushResult result = await TestSender.Create(handler).SendAsync(Sub(client), WebPushMessage.Text("T"));

        Assert.Equal(expected, result.Status);
        Assert.Equal((int)http, result.StatusCode);
    }

    [Fact]
    public async Task Expired_sets_should_delete_and_transient_sets_should_retry()
    {
        using var client = TestCrypto.GenerateClient();

        WebPushResult gone = await TestSender.Create(new RecordingHandler(HttpStatusCode.Gone)).SendAsync(Sub(client), WebPushMessage.Text("T"));
        WebPushResult busy = await TestSender.Create(new RecordingHandler(HttpStatusCode.TooManyRequests)).SendAsync(Sub(client), WebPushMessage.Text("T"));

        Assert.True(gone.ShouldDelete);
        Assert.False(gone.ShouldRetry);
        Assert.True(busy.ShouldRetry);
        Assert.False(busy.ShouldDelete);
    }

    [Fact]
    public async Task Network_failure_maps_to_transient()
    {
        using var client = TestCrypto.GenerateClient();
        var sender = new WebPushSender(new HttpClient(new ThrowingHandler()), TestSender.Options());

        WebPushResult result = await sender.SendAsync(Sub(client), WebPushMessage.Text("T"));

        Assert.Equal(WebPushStatus.TransientFailure, result.Status);
        Assert.True(result.ShouldRetry);
        Assert.Null(result.StatusCode);
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("connection refused");
    }

    [Theory]
    [InlineData("http://push.example/abc")]   // not https
    [InlineData("ftp://push.example/abc")]     // wrong scheme
    [InlineData("/relative/path")]             // not absolute
    [InlineData("not a url")]
    public async Task Rejects_non_https_endpoints(string endpoint)
    {
        using var client = TestCrypto.GenerateClient();
        var handler = new RecordingHandler();
        var sub = new PushSubscription(endpoint, client.P256dhB64, client.AuthB64);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            TestSender.Create(handler).SendAsync(sub, WebPushMessage.Text("T")));
        Assert.Null(handler.Request); // never left the process.
    }

    [Fact]
    public async Task Rejects_auth_secret_of_wrong_length()
    {
        using var client = TestCrypto.GenerateClient();
        var handler = new RecordingHandler();
        // Valid https endpoint + valid p256dh, but a 9-byte auth (RFC 8291 requires 16).
        var sub = new PushSubscription(TestSender.Endpoint, client.P256dhB64,
            System.Buffers.Text.Base64Url.EncodeToString(new byte[9]));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            TestSender.Create(handler).SendAsync(sub, WebPushMessage.Text("T")));
    }

    [Fact]
    public async Task Reuses_the_vapid_header_across_sends_to_the_same_authority()
    {
        var handler = new RecordingHandler();
        var sender = TestSender.Create(handler);
        using var client = TestCrypto.GenerateClient();
        // Two different endpoints, same authority → same cached VAPID token.
        var a = new PushSubscription("https://fcm.googleapis.com/fcm/send/a", client.P256dhB64, client.AuthB64);
        var b = new PushSubscription("https://fcm.googleapis.com/fcm/send/b", client.P256dhB64, client.AuthB64);

        await sender.SendAsync(a, WebPushMessage.Text("T"));
        string first = handler.Request!.Headers.GetValues("Authorization").Single();
        await sender.SendAsync(b, WebPushMessage.Text("T"));
        string second = handler.Request!.Headers.GetValues("Authorization").Single();

        Assert.Equal(first, second);
    }

    [Fact]
    public void Constructor_validates_options()
    {
        var bad = new WebPushOptions { Subject = "mailto:x@y.com" }; // missing keys.
        Assert.Throws<InvalidOperationException>(() => new WebPushSender(new HttpClient(new RecordingHandler()), bad));
    }
}
