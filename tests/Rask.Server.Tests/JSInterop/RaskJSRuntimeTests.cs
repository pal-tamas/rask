using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Rask.Core;
using Rask.Core.Components;
using Rask.Server.Tests.Infrastructure;

#pragma warning disable RASK019 // test-helper Components predate framework-managed <head>

namespace Rask.Server.Tests.JSInterop;

public class RaskJSRuntimeTests
{
    [Fact]
    public async Task InvokeAsync_RoundTrip_QueuesInvokeAndCompletesTcs()
    {
        // Component calls IJSRuntime.InvokeAsync<string> in OnRendered(true). Server queues
        // the invoke onto the next outbound frame. Test acts as the JS client: receives
        // jsInvokes, asserts shape, sends a jsResult back; the component's awaiting Task
        // resolves and posts the result into a publicly observable TCS.
        using var host = RaskTestHost.Create<JsRoundTripApp>();
        var initialResponse = await host.Http.GetAsync("/");
        var initialHtml = await initialResponse.Content.ReadAsStringAsync();
        var sessionId = Markup.SessionId(initialHtml);

        using var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
        await ws.SendJsonAsync(new { type = "hello", session = sessionId });

        // First frame after hello: server re-renders and ships the pending jsInvoke.
        var first = await ws.TryReceiveTextAsync(TimeSpan.FromSeconds(2));
        Assert.NotNull(first);
        using (var doc = JsonDocument.Parse(first!))
        {
            Assert.True(doc.RootElement.TryGetProperty("jsInvokes", out var jsInvokes),
                "expected jsInvokes array on first post-hello frame, got: " + first);
            Assert.Equal(JsonValueKind.Array, jsInvokes.ValueKind);
            Assert.Equal(1, jsInvokes.GetArrayLength());

            var invoke = jsInvokes[0];
            Assert.Equal("sessionStorage.getItem", invoke.GetProperty("identifier").GetString());
            Assert.True(invoke.TryGetProperty("argsJson", out var argsJson));
            Assert.Equal("[\"my-key\"]", argsJson.GetString());

            var taskId = invoke.GetProperty("id").GetInt64();
            // Send jsResult: emulate JS-side returning "stored-value" for sessionStorage.getItem.
            await ws.SendJsonAsync(new { type = "jsResult", id = taskId, success = true, result = "stored-value" });
        }

        // Wait for the TCS to be completed by the await-continuation in the component.
        var observed = await JsRoundTripApp.LastResult.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal("stored-value", observed);
    }

    [Fact]
    public async Task InvokeAsync_ErrorPath_PropagatesAsJSException()
    {
        using var host = RaskTestHost.Create<JsErrorApp>();
        var initialHtml = await host.Http.GetStringAsync("/");
        var sessionId = Markup.SessionId(initialHtml);

        using var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
        await ws.SendJsonAsync(new { type = "hello", session = sessionId });
        var first = await ws.TryReceiveTextAsync(TimeSpan.FromSeconds(2));
        Assert.NotNull(first);
        using var doc = JsonDocument.Parse(first!);
        var invoke = doc.RootElement.GetProperty("jsInvokes")[0];
        var taskId = invoke.GetProperty("id").GetInt64();
        await ws.SendJsonAsync(new { type = "jsResult", id = taskId, success = false, error = "TypeError: nope" });

        var ex = await JsErrorApp.Caught.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.IsType<JSException>(ex);
        Assert.Contains("TypeError: nope", ex.Message);
    }

    [Fact]
    public async Task InvokeAsync_FromOnClickAsync_DoesNotDeadlockReceiveLoop()
    {
        // Regression: if the WS receive loop blocks on handler completion AND the handler
        // awaits IJSRuntime.InvokeVoidAsync, the handler can't complete (waiting for
        // jsResult) and the receive loop can't pull the jsResult (waiting for the handler).
        // The dispatch must spawn the handler so the loop keeps reading; cross-handler
        // ordering is preserved by session.Lock.
        using var host = RaskTestHost.Create<JsClickApp>();
        var initialHtml = await host.Http.GetStringAsync("/");
        var sessionId = Markup.SessionId(initialHtml);

        using var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
        await ws.SendJsonAsync(new { type = "hello", session = sessionId });

        // No hello-time frame to drain: JsClickApp has no pending state mutations or JS
        // invokes during the GET→hello handoff, so FlushPendingRenderAsync skips. Read
        // the click handler id straight from the GET HTML.
        var clickIdMatch = Regex.Match(initialHtml, @"data-rask-on-click=""([^""]+)""");
        Assert.True(clickIdMatch.Success, "expected data-rask-on-click in initial HTML: " + initialHtml);
        var clickId = clickIdMatch.Groups[1].Value;

        // Click the button → handler runs SetAsync → awaits InvokeVoidAsync. With the
        // fire-and-forget dispatch, the receive loop must still be reading.
        await ws.SendJsonAsync(new { id = clickId, type = "click" });

        // We expect the next frame to carry a jsInvokes entry for sessionStorage.setItem.
        var clickFrame = await ws.TryReceiveTextAsync(TimeSpan.FromSeconds(2));
        Assert.NotNull(clickFrame);
        using var doc = JsonDocument.Parse(clickFrame!);
        Assert.True(doc.RootElement.TryGetProperty("jsInvokes", out var jsInvokes),
            "expected jsInvokes on click frame, got: " + clickFrame);
        var taskId = jsInvokes[0].GetProperty("id").GetInt64();

        // Send jsResult — receive loop must process this concurrently with the still-running
        // handler. If it deadlocks, this message is never read and the test times out.
        await ws.SendJsonAsync(new { type = "jsResult", id = taskId, success = true });

        // The handler completes, sets _status, and re-renders. Awaiting on a TCS sidesteps
        // having to poll for the post-completion render frame.
        await JsClickApp.Completed.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal("done", JsClickApp.LastStatus);
    }

    [Fact]
    public async Task InvokeVoidAsync_FromOnRenderedAsync_NoFirstRenderGuard_DoesNotRenderStorm()
    {
        // Regression for the memory leak: a component that does
        //     protected override async Task OnRenderedAsync(bool firstRender) =>
        //         await js.InvokeVoidAsync("foo");
        // (no `if (!firstRender) return;` guard) used to drive an infinite render
        // loop. Two paths fed it: (1) the OnRenderedAsync continuation auto-rerendered
        // on completion; (2) BeginInvokeJS unconditionally called RequestRenderAsync,
        // which scheduled another render → another OnRenderedAsync → another
        // BeginInvokeJS → loop. Both paths are closed; this test exercises the
        // in-render-walk path through a real session.
        using var host = RaskTestHost.Create<JsRenderStormApp>();
        var initialHtml = await host.Http.GetStringAsync("/");
        var sessionId = Markup.SessionId(initialHtml);

        using var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
        await ws.SendJsonAsync(new { type = "hello", session = sessionId });

        // First post-hello frame: server renders, OnRenderedAsync fires, queues one jsInvoke.
        var first = await ws.TryReceiveTextAsync(TimeSpan.FromSeconds(2));
        Assert.NotNull(first);
        // First frame typically carries 2 jsInvokes — one from the HTTP GET render
        // (firstRender=true) and one from the post-hello re-render (firstRender=false),
        // because OnRenderedAsync runs unconditionally. Reply to all of them.
        long[] taskIds;
        using (var doc = JsonDocument.Parse(first!))
        {
            var jsInvokes = doc.RootElement.GetProperty("jsInvokes");
            taskIds = new long[jsInvokes.GetArrayLength()];
            for (var i = 0; i < taskIds.Length; i++)
            {
                taskIds[i] = jsInvokes[i].GetProperty("id").GetInt64();
            }
        }

        // Reply to every pending invoke. Pre-fix: each reply triggers a fresh
        // render → re-fires OnRenderedAsync → fresh jsInvoke → another reply needed
        // → forever. Post-fix: no extra render scheduled.
        foreach (var id in taskIds)
        {
            await ws.SendJsonAsync(new { type = "jsResult", id, success = true });
        }

        // Drain anything that arrives in the next 500 ms. A correctly-behaving server
        // ships at most one trailing frame (the no-op "everything's flushed" rerender);
        // a looping server ships an unbounded stream and we'd see double-digit frames.
        var trailing = 0;
        var trailingInvokes = 0;
        var deadline = DateTime.UtcNow + TimeSpan.FromMilliseconds(500);
        while (DateTime.UtcNow < deadline)
        {
            var frame = await ws.TryReceiveTextAsync(TimeSpan.FromMilliseconds(100));
            if (frame is null)
            {
                continue;
            }

            trailing++;
            using var doc = JsonDocument.Parse(frame);
            if (doc.RootElement.TryGetProperty("jsInvokes", out var arr))
            {
                trailingInvokes += arr.GetArrayLength();
            }
        }

        Assert.True(trailing < 5,
            $"render storm: received {trailing} trailing frames carrying {trailingInvokes} jsInvokes");
        Assert.True(trailingInvokes < 5,
            $"render storm: {trailingInvokes} extra jsInvokes queued after a single reply");
    }

    [Fact]
    public async Task InvokeAsync_OutsideSessionScope_Throws()
    {
        // Resolve RaskJSRuntime from a DI scope that doesn't go through LiveSessionStore.Create —
        // the LiveSessionAccessor.Session stays null, and any interop call must throw with a
        // clear error rather than crashing somewhere deeper in the WS dispatch.
        var services = new ServiceCollection();
        services.AddRouting();
        services.AddRask();
        await using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();

        var js = scope.ServiceProvider.GetRequiredService<IJSRuntime>();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await js.InvokeAsync<string>("anything", "arg").AsTask());
        Assert.Contains("Rask session", ex.Message);
    }
}

#pragma warning disable RASK014 // private test-helper Component subclasses
internal sealed class JsRoundTripApp : Component
{
    private readonly IJSRuntime _js;

    public JsRoundTripApp(IJSRuntime js) => _js = js;

    public static TaskCompletionSource<string?> LastResult { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    protected override Component? Render() =>
    [
        Doctype(),
        new Html()[new Head()[new Title()["t"]], new Body()[Text("ready")]]
    ];

    protected override async Task OnRenderedAsync(bool firstRender)
    {
        if (!firstRender)
        {
            return;
        }

        try
        {
            var value = await _js.InvokeAsync<string?>("sessionStorage.getItem", "my-key");
            LastResult.TrySetResult(value);
        }
        catch (Exception ex)
        {
            LastResult.TrySetException(ex);
        }
    }
}

internal sealed class JsClickApp : Component
{
    public static string? LastStatus;

    private readonly IJSRuntime _js;

    public JsClickApp(IJSRuntime js) => _js = js;
    public static TaskCompletionSource Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    protected override Component? Render() =>
    [
        Doctype(),
        new Html()[new Head()[new Title()["t"]],
            new Body()[
                Button(OnClickAsync: SetAsync)["set"]
            ]]
    ];

    private async Task SetAsync()
    {
        await _js.InvokeVoidAsync("sessionStorage.setItem", "k", "v");
        LastStatus = "done";
        Completed.TrySetResult();
    }
}

internal sealed class JsRenderStormApp : Component
{
    private readonly IJSRuntime _js;

    public JsRenderStormApp(IJSRuntime js) => _js = js;

    protected override Component? Render() =>
    [
        Doctype(),
        new Html()[new Head()[new Title()["t"]], new Body()[Text("ready")]]
    ];

    // Intentionally NO firstRender guard — the whole point is to assert the framework
    // doesn't loop even with this anti-pattern. Mirrors the original CodeSample shape
    // that triggered the leak.
    protected override async Task OnRenderedAsync(bool firstRender) =>
        await _js.InvokeVoidAsync("noop");
}

internal sealed class JsErrorApp : Component
{
    private readonly IJSRuntime _js;

    public JsErrorApp(IJSRuntime js) => _js = js;

    public static TaskCompletionSource<Exception> Caught { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    protected override Component? Render() =>
    [
        Doctype(),
        new Html()[new Head()[new Title()["t"]], new Body()[Text("ready")]]
    ];

    protected override async Task OnRenderedAsync(bool firstRender)
    {
        if (!firstRender)
        {
            return;
        }

        try
        {
            await _js.InvokeAsync<string?>("nonexistent.method");
            Caught.TrySetException(new InvalidOperationException("expected throw, got success"));
        }
        catch (Exception ex)
        {
            Caught.TrySetResult(ex);
        }
    }
}
#pragma warning restore RASK014
