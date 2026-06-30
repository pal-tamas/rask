using System.Buffers.Text;
using System.Net;
using System.Text;
using System.Text.Json;
using Rask.Example.Wasm.Host.Tests.Infrastructure;

namespace Rask.Example.Wasm.Host.Tests.Hosting;

// The Web Push backend (PushBackend) is wired into the host pipeline alongside the WASM bundle. These
// exercise the deterministic, offline parts of that wiring — DI resolves IWebPushSender + the store,
// the endpoints are mapped, and JSON binds. The encryption/VAPID correctness itself is covered by
// Rask.WebPush.Tests; delivery can't run without a real push service.
public sealed class PushEndpointsTests
{
    [Fact]
    public async Task Key_endpoint_returns_a_65_byte_vapid_public_key()
    {
        using var bundle = new FakeBundle();
        await using var host = await ExampleHostTestServer.CreateAsync(bundle.Path);

        var response = await host.Http.GetAsync("/_push/key");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        string? key = doc.RootElement.GetProperty("publicKey").GetString();
        Assert.False(string.IsNullOrEmpty(key));
        Assert.Equal(65, Base64Url.DecodeFromChars(key).Length); // uncompressed P-256 point.
    }

    [Fact]
    public async Task Subscribe_then_broadcast_to_empty_store_is_a_no_op()
    {
        using var bundle = new FakeBundle();
        await using var host = await ExampleHostTestServer.CreateAsync(bundle.Path);

        // Broadcast with nothing stored: 200 with zero sends (no network).
        var empty = await host.Http.PostAsync("/_push/send", JsonContent(new { title = "Hi" }));
        Assert.Equal(HttpStatusCode.OK, empty.StatusCode);
        using var emptyDoc = JsonDocument.Parse(await empty.Content.ReadAsStringAsync());
        Assert.Equal(0, emptyDoc.RootElement.GetProperty("sent").GetInt32());

        // Storing a subscription succeeds with 204.
        var subscribe = await host.Http.PostAsync("/_push/subscribe", JsonContent(new
        {
            endpoint = "https://push.example/abc",
            p256dh = "BPay_demo",
            auth = "demoAuth"
        }));
        Assert.Equal(HttpStatusCode.NoContent, subscribe.StatusCode);
    }

    private static StringContent JsonContent(object value) =>
        new(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json");
}
