using System.Text.Json;
using Rask.Core.Live;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Live;

/// <summary>
///     Dispatch used to key on the message id alone. Handler ids are positional per render, so a frame
///     that outlived the render it was issued against resolves to whatever now sits in that slot — an
///     <c>input</c> message landing on a parameterless callback ran it, silently. These pin the
///     cross-check of the frame's declared <c>type</c> against the argument the handler demands (#587).
/// </summary>
public class HandlerFrameTypeTests
{
    private static JsonElement Frame(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public async Task An_input_frame_does_not_fire_a_parameterless_handler()
    {
        // The reported misfire: h0 was an input handler when the client read the id, and is a click
        // handler by the time the message arrives.
        var fired = 0;
        var view = new StubComponent(() => Button(OnClick: () => fired++)["x"]);
        view.RenderAsLiveRoot();

        var ok = await view.TryInvokeHandlerAsync("h0", Frame("""{"id":"h0","type":"input","value":"x"}"""));

        Assert.False(ok);
        Assert.Equal(0, fired);
    }

    [Fact]
    public async Task A_click_frame_does_not_fire_a_value_handler()
    {
        // The mirror image, and the reason the check is not just "a data frame needs a data handler":
        // a click carries nothing, so feeding it to a value handler would silently set an empty string.
        var captured = "untouched";
        var view = new StubComponent(() => Input<string>(OnInput: v => captured = v));
        view.RenderAsLiveRoot();

        var ok = await view.TryInvokeHandlerAsync("h0", Frame("""{"id":"h0","type":"click"}"""));

        Assert.False(ok);
        Assert.Equal("untouched", captured);
    }

    [Fact]
    public async Task A_submit_frame_does_not_fire_a_parameterless_handler()
    {
        var fired = 0;
        var view = new StubComponent(() => Button(OnClick: () => fired++)["x"]);
        view.RenderAsLiveRoot();

        var ok = await view.TryInvokeHandlerAsync(
            "h0", Frame("""{"id":"h0","type":"submit","form":{"name":"Bob"}}"""));

        Assert.False(ok);
        Assert.Equal(0, fired);
    }

    [Fact]
    public async Task A_keyboard_frame_does_not_fire_a_parameterless_handler()
    {
        var fired = 0;
        var view = new StubComponent(() => Button(OnClick: () => fired++)["x"]);
        view.RenderAsLiveRoot();

        var ok = await view.TryInvokeHandlerAsync(
            "h0", Frame("""{"id":"h0","type":"keydown","key":"Enter","code":"Enter"}"""));

        Assert.False(ok);
        Assert.Equal(0, fired);
    }

    [Fact]
    public async Task A_click_frame_fires_a_parameterless_handler()
    {
        var fired = 0;
        var view = new StubComponent(() => Button(OnClick: () => fired++)["x"]);
        view.RenderAsLiveRoot();

        var ok = await view.TryInvokeHandlerAsync("h0", Frame("""{"id":"h0","type":"click"}"""));

        Assert.True(ok);
        Assert.Equal(1, fired);
    }

    [Fact]
    public async Task A_change_frame_fires_a_value_handler()
    {
        // input and change both feed a string handler — the client sends `change` for a select and a
        // committed text edit, `input` for the coalesced keystroke.
        var captured = string.Empty;
        var view = new StubComponent(() => Input<string>(OnInput: v => captured = v));
        view.RenderAsLiveRoot();

        var ok = await view.TryInvokeHandlerAsync(
            "h0", Frame("""{"id":"h0","type":"change","value":"hello"}"""));

        Assert.True(ok);
        Assert.Equal("hello", captured);
    }

    [Fact]
    public async Task A_focus_frame_fires_a_parameterless_handler()
    {
        // Same shape, different event: both carry nothing, so this one still dispatches. Telling them
        // apart would need the event name stored per handler — see the remarks on HandlerFrameShape.
        var fired = 0;
        var view = new StubComponent(() => Div(OnFocus: () => fired++));
        view.RenderAsLiveRoot();

        var ok = await view.TryInvokeHandlerAsync("h0", Frame("""{"id":"h0","type":"focus"}"""));

        Assert.True(ok);
        Assert.Equal(1, fired);
    }

    [Fact]
    public async Task A_frame_type_this_build_does_not_know_is_still_dispatched()
    {
        // Forward compatibility: a browser holding a cached client from another deploy must not have its
        // events swallowed. Only a type some OTHER shape claims is refused.
        var fired = 0;
        var view = new StubComponent(() => Button(OnClick: () => fired++)["x"]);
        view.RenderAsLiveRoot();

        var ok = await view.TryInvokeHandlerAsync("h0", Frame("""{"id":"h0","type":"someFutureEvent"}"""));

        Assert.True(ok);
        Assert.Equal(1, fired);
    }

    [Fact]
    public async Task A_frame_with_no_type_is_still_dispatched()
    {
        // RaskTest dispatches a bare payload, and so do plenty of unit tests. No claim, nothing to check.
        var fired = 0;
        var view = new StubComponent(() => Button(OnClick: () => fired++)["x"]);
        view.RenderAsLiveRoot();

        var ok = await view.TryInvokeHandlerAsync("h0", Frame("""{"value":"x"}"""));

        Assert.True(ok);
        Assert.Equal(1, fired);
    }

    [Fact]
    public async Task A_files_frame_does_not_fire_a_value_handler()
    {
        // Both carry data, so neither is parameterless — the shapes still have to match.
        var captured = "untouched";
        var view = new StubComponent(() => Input<string>(OnInput: v => captured = v));
        view.RenderAsLiveRoot();

        var ok = await view.TryInvokeHandlerAsync(
            "h0",
            Frame("""{"id":"h0","type":"files","files":[{"token":"t","name":"a.txt","size":1}]}"""));

        Assert.False(ok);
        Assert.Equal("untouched", captured);
    }

    [Fact]
    public async Task A_scroll_frame_fires_a_scroll_handler()
    {
        ScrollEvent? captured = null;
        var view = new StubComponent(() => Div(OnScroll: e => captured = e));
        view.RenderAsLiveRoot();

        var ok = await view.TryInvokeHandlerAsync(
            "h0",
            Frame("""{"id":"h0","type":"scroll","scrollTop":40,"clientHeight":10,"scrollHeight":100}"""));

        Assert.True(ok);
        Assert.Equal(40, captured?.ScrollTop);
    }
}
