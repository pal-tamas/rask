using System.Text;
using System.Text.Json;

namespace Rask.WebPush.Tests;

// End-to-end proof: send through WebPushSender, capture the encrypted body, then decrypt it with the
// simulated browser's private keys and assert the plaintext. If RFC 8291 (ECDH/HKDF/AES-GCM) or the
// payload JSON shape were wrong, the decrypt would fail or the JSON would not match.
public sealed class Rfc8291RoundTripTests
{
    private static async Task<string> RoundTrip(WebPushMessage message, TestCrypto.Client client)
    {
        var handler = new RecordingHandler();
        var sender = TestSender.Create(handler);
        var sub = new PushSubscription(TestSender.Endpoint, client.P256dhB64, client.AuthB64);

        WebPushResult result = await sender.SendAsync(sub, message);

        Assert.True(result.IsSuccess);
        byte[] plaintext = TestCrypto.Decrypt(handler.Body, client);
        return Encoding.UTF8.GetString(plaintext);
    }

    [Fact]
    public async Task Typed_message_decrypts_to_the_service_worker_json_shape()
    {
        using var client = TestCrypto.GenerateClient();

        string json = await RoundTrip(WebPushMessage.Text("Hello", "World", "/inbox"), client);

        using var doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;
        Assert.Equal("Hello", root.GetProperty("title").GetString());
        Assert.Equal("World", root.GetProperty("body").GetString());
        // The click URL must live UNDER "data" so rask-sw.js's notificationclick handler finds it.
        Assert.Equal("/inbox", root.GetProperty("data").GetProperty("url").GetString());
        // Null fields are omitted.
        Assert.False(root.TryGetProperty("icon", out _));
    }

    [Fact]
    public async Task Raw_payload_is_sent_verbatim()
    {
        using var client = TestCrypto.GenerateClient();
        const string raw = "{\"title\":\"Custom\",\"foo\":[1,2,3]}";

        string json = await RoundTrip(WebPushMessage.Raw(raw), client);

        Assert.Equal(raw, json);
    }

    [Fact]
    public async Task Each_send_uses_a_fresh_salt_and_ephemeral_key_yet_both_decrypt()
    {
        using var client = TestCrypto.GenerateClient();
        var sub = new PushSubscription(TestSender.Endpoint, client.P256dhB64, client.AuthB64);

        var h1 = new RecordingHandler();
        var h2 = new RecordingHandler();
        await TestSender.Create(h1).SendAsync(sub, WebPushMessage.Text("A"));
        await TestSender.Create(h2).SendAsync(sub, WebPushMessage.Text("A"));

        // Same plaintext, but the ciphertext differs (random salt + ephemeral key each time)...
        Assert.False(h1.Body.AsSpan().SequenceEqual(h2.Body));
        // ...and each still decrypts correctly.
        Assert.Contains("\"title\":\"A\"", Encoding.UTF8.GetString(TestCrypto.Decrypt(h1.Body, client)), StringComparison.Ordinal);
        Assert.Contains("\"title\":\"A\"", Encoding.UTF8.GetString(TestCrypto.Decrypt(h2.Body, client)), StringComparison.Ordinal);
    }
}
