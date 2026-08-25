using System.Text;
using Rask.Native.Tests.Infrastructure;

namespace Rask.Native.Tests.Session;

/// <summary>
///     The head's half of chrome in the remote models: a descriptor arriving from an app that runs
///     elsewhere is applied to real platform bars, and a press on one is sent back to that app.
/// </summary>
/// <remarks>
///     There is no session here holding the callbacks — the bar was declared by a server or WASM app, and
///     its <c>OnClick</c> lives there. The head draws and notices; the meaning stays with the app. These
///     tests pin that split, including the parts that must NOT happen: a chrome message must never fall
///     through to the session's event router, which would treat it as an unknown DOM event.
/// </remarks>
public class RemoteChromeRoutingTests
{
    private static ReadOnlyMemory<byte> Json(string json) => Encoding.UTF8.GetBytes(json);

    [Fact]
    public async Task A_descriptor_from_a_remote_app_reaches_the_platform_bars()
    {
        var chrome = new FakeNativeChrome();

        var handled = await NativeShellChrome.TryApplyAsync(
            Json("""{"type":"chrome","data":"{\"header\":{\"title\":\"Inbox\"}}"}"""), chrome);

        Assert.True(handled);
        Assert.Single(chrome.Pushed);
        Assert.Contains("Inbox", chrome.LastJson, StringComparison.Ordinal);
    }

    /// <summary>Anything that is not chrome is left alone, so the ordinary router still sees it.</summary>
    [Fact]
    public async Task Another_kind_of_message_is_not_claimed()
    {
        var chrome = new FakeNativeChrome();

        var handled = await NativeShellChrome.TryApplyAsync(
            Json("""{"type":"capability","name":"share"}"""), chrome);

        Assert.False(handled);
        Assert.Empty(chrome.Pushed);
    }

    /// <summary>
    ///     A head that draws no bars still consumes the message. Passing it on would only get it routed as
    ///     an unknown event.
    /// </summary>
    [Fact]
    public async Task A_head_with_no_bars_consumes_it_anyway()
    {
        var handled = await NativeShellChrome.TryApplyAsync(
            Json("""{"type":"chrome","data":"{}"}"""), chrome: null);

        Assert.True(handled);
    }

    /// <summary>Malformed input is discarded rather than thrown, so one bad frame cannot kill the app.</summary>
    [Fact]
    public async Task A_malformed_message_is_discarded()
    {
        var chrome = new FakeNativeChrome();

        var handled = await NativeShellChrome.TryApplyAsync(Json("{ not json"), chrome);

        Assert.False(handled);
        Assert.Empty(chrome.Pushed);
    }

    [Fact]
    public void A_tap_is_sent_back_to_the_page_that_owns_the_callback()
    {
        var script = NativeShellChrome.TapScriptFor(Json("""{"type":"nativeTap","id":"h.trailing.0"}"""));

        Assert.NotNull(script);
        Assert.Contains("__raskNative.chromeTap", script, StringComparison.Ordinal);
        Assert.Contains("h.trailing.0", script, StringComparison.Ordinal);
    }

    /// <summary>
    ///     An id is not interpolated raw. It reaches the page as JavaScript source, so a quote in it would
    ///     otherwise end the string literal and let the rest run as code.
    /// </summary>
    [Fact]
    public void A_tap_id_is_escaped_before_it_becomes_script()
    {
        var script = NativeShellChrome.TapScriptFor(
            Json("""{"type":"nativeTap","id":"a\");alert(1);(\""}"""));

        Assert.NotNull(script);
        // The breakout sequence, not the payload: the injected text may appear, but only as inert
        // characters INSIDE the string literal. What must not appear is a quote that closes it.
        Assert.DoesNotContain("\");alert", script, StringComparison.Ordinal);
        Assert.Contains("\\u0022", script, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{"type":"back"}""")]
    [InlineData("""{"type":"nativeTap"}""")]
    [InlineData("not json at all")]
    public void Anything_that_is_not_a_tap_produces_no_script(string json)
    {
        Assert.Null(NativeShellChrome.TapScriptFor(Json(json)));
    }
}
