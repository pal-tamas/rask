using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Rask.Client.Browser;
using Rask.Core.Browser;

namespace Rask.Native.Tests;

/// <summary>
///     The capability bridge: a <c>{ type:"capability" }</c> envelope routes to the native backend the head
///     registered, and <b>answers</b>. The answer is the point — before it there was no correlation id and no
///     reply path, so the only capability that could work was one that returns nothing (share). Everything
///     else was accepted and silently dropped, leaving the page's await pending for ever.
/// </summary>
public class NativeCapabilitiesTests
{
    [Fact]
    public async Task A_capability_reaches_its_backend_and_the_page_is_told_it_worked()
    {
        var share = new RecordingShare();
        var replies = new List<string>();

        var handled = await NativeCapabilities.TryHandleAsync(
            Msg("""{"type":"capability","id":"1","component":"share","op":"share","data":"{\"title\":\"Rask\"}"}"""),
            Services(s => s.AddSingleton<IShare>(share)),
            Capture(replies));

        Assert.True(handled);
        Assert.Equal("Rask", share.Last?.Title);

        var reply = Reply(replies);
        Assert.Equal("1", reply.GetProperty("id").GetString());
        Assert.True(reply.GetProperty("success").GetBoolean());
    }

    /// <summary>
    ///     The half that did not exist: a capability with a RESULT. Twenty-two of the thirty-five members
    ///     across the fifteen backends return a value, and none of them could cross the bridge before.
    /// </summary>
    [Fact]
    public async Task A_capability_that_returns_a_value_sends_it_back()
    {
        var replies = new List<string>();

        var handled = await NativeCapabilities.TryHandleAsync(
            Msg("""{"type":"capability","id":"7","component":"clipboard","op":"readText"}"""),
            Services(s => s.AddSingleton<IClipboard>(new StubClipboard("copied"))),
            Capture(replies));

        Assert.True(handled);

        var reply = Reply(replies);
        Assert.True(reply.GetProperty("success").GetBoolean());
        // The result rides as JSON so the page parses it into whatever shape its own wrapper expects.
        Assert.Equal("\"copied\"", reply.GetProperty("result").GetString());
    }

    /// <summary>
    ///     A backend that throws must still answer. A page is awaiting; the only outcome worse than an error
    ///     is a promise that never settles.
    /// </summary>
    [Fact]
    public async Task A_backend_that_throws_answers_with_the_reason()
    {
        var replies = new List<string>();

        await NativeCapabilities.TryHandleAsync(
            Msg("""{"type":"capability","id":"2","component":"clipboard","op":"readText"}"""),
            Services(s => s.AddSingleton<IClipboard>(new ThrowingClipboard())),
            Capture(replies));

        var reply = Reply(replies);
        Assert.False(reply.GetProperty("success").GetBoolean());
        Assert.Contains("clipboard is on fire", reply.GetProperty("error").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unknown_capability_answers_instead_of_going_quiet()
    {
        var replies = new List<string>();

        var handled = await NativeCapabilities.TryHandleAsync(
            Msg("""{"type":"capability","id":"3","component":"teleport","op":"go"}"""),
            Services(_ => { }),
            Capture(replies));

        Assert.True(handled);

        var reply = Reply(replies);
        Assert.False(reply.GetProperty("success").GetBoolean());
        Assert.Contains("teleport", reply.GetProperty("error").GetString(), StringComparison.Ordinal);
    }

    /// <summary>An op whose backend the head never registered is a failure the page can read, not a hang.</summary>
    [Fact]
    public async Task A_capability_with_no_backend_registered_answers_with_the_reason()
    {
        var replies = new List<string>();

        await NativeCapabilities.TryHandleAsync(
            Msg("""{"type":"capability","id":"4","component":"clipboard","op":"readText"}"""),
            Services(_ => { }),
            Capture(replies));

        var reply = Reply(replies);
        Assert.False(reply.GetProperty("success").GetBoolean());
        Assert.Contains("IClipboard", reply.GetProperty("error").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_non_capability_message_is_left_for_the_head()
    {
        var replies = new List<string>();

        var handled = await NativeCapabilities.TryHandleAsync(
            Msg("""{"type":"navigate","path":"/x"}"""), Services(_ => { }), Capture(replies));

        Assert.False(handled);
        Assert.Empty(replies);
    }

    /// <summary>A call the page does not await needs no reply, and must not manufacture one.</summary>
    [Fact]
    public async Task An_envelope_with_no_id_is_run_without_a_reply()
    {
        var share = new RecordingShare();
        var replies = new List<string>();

        await NativeCapabilities.TryHandleAsync(
            Msg("""{"type":"capability","component":"share","op":"share","data":"{\"title\":\"Rask\"}"}"""),
            Services(s => s.AddSingleton<IShare>(share)),
            Capture(replies));

        Assert.Equal("Rask", share.Last?.Title);
        Assert.Empty(replies);
    }

    [Fact]
    public async Task A_malformed_message_is_discarded_rather_than_thrown()
    {
        var replies = new List<string>();

        var handled = await NativeCapabilities.TryHandleAsync(
            Msg("{ not json"), Services(_ => { }), Capture(replies));

        Assert.False(handled);
        Assert.Empty(replies);
    }

    [Fact]
    public void The_bridge_script_advertises_exactly_what_it_was_given()
    {
        var script = NativeCapabilities.BridgeScript(["share", "geolocation"]);

        Assert.Contains("""n.capabilities = ["share","geolocation"];""", script, StringComparison.Ordinal);
        // The promise table is what makes a result possible at all.
        Assert.Contains("capabilityResult", script, StringComparison.Ordinal);
    }

    /// <summary>
    ///     A head with no platform module promises nothing, so the page uses its own web APIs — the "no
    ///     IsNative branch in app code" property, from the other direction.
    /// </summary>
    [Fact]
    public void A_head_that_backs_nothing_advertises_nothing()
    {
        var script = NativeCapabilities.BridgeScript([]);

        Assert.Contains("n.capabilities = [];", script, StringComparison.Ordinal);
    }

    private static ReadOnlyMemory<byte> Msg(string json) => Encoding.UTF8.GetBytes(json);

    private static IServiceProvider Services(Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection();
        configure(services);
        return services.BuildServiceProvider();
    }

    private static Func<string, ValueTask> Capture(List<string> into) =>
        script =>
        {
            into.Add(script);
            return default;
        };

    // The reply arrives as `window.__raskNative.capabilityResult("<escaped json>")`; unwrap it to the object.
    private static JsonElement Reply(List<string> replies)
    {
        var script = Assert.Single(replies);
        var open = script.IndexOf('"', StringComparison.Ordinal);
        var close = script.LastIndexOf('"');
        var literal = script[open..(close + 1)];
        var json = JsonSerializer.Deserialize<string>(literal)!;
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    private sealed class RecordingShare : IShare
    {
        public ShareData? Last { get; private set; }

        public ValueTask ShareAsync(ShareData data)
        {
            Last = data;
            return default;
        }

        public ValueTask<bool> CanShareAsync(ShareData? data = null) => ValueTask.FromResult(true);
    }

    private sealed class StubClipboard(string text) : IClipboard
    {
        public ValueTask WriteTextAsync(string value) => default;

        public ValueTask<string> ReadTextAsync() => ValueTask.FromResult(text);
    }

    private sealed class ThrowingClipboard : IClipboard
    {
        public ValueTask WriteTextAsync(string value) => default;

        public ValueTask<string> ReadTextAsync() => throw new InvalidOperationException("the clipboard is on fire");
    }
}
