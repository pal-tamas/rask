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
        var sessionId = ExtractSessionId(initialHtml);

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
            await ws.SendJsonAsync(new
            {
                type = "jsResult",
                id = taskId,
                success = true,
                result = "stored-value"
            });
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
        var sessionId = ExtractSessionId(initialHtml);

        using var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
        await ws.SendJsonAsync(new { type = "hello", session = sessionId });
        var first = await ws.TryReceiveTextAsync(TimeSpan.FromSeconds(2));
        Assert.NotNull(first);
        using var doc = JsonDocument.Parse(first!);
        var invoke = doc.RootElement.GetProperty("jsInvokes")[0];
        var taskId = invoke.GetProperty("id").GetInt64();
        await ws.SendJsonAsync(new
        {
            type = "jsResult",
            id = taskId,
            success = false,
            error = "TypeError: nope"
        });

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
        var sessionId = ExtractSessionId(initialHtml);

        using var ws = await host.WebSockets.ConnectAsync(host.WebSocketUri, CancellationToken.None);
        await ws.SendJsonAsync(new { type = "hello", session = sessionId });
        var helloFrame = await ws.TryReceiveTextAsync(TimeSpan.FromSeconds(2));
        Assert.NotNull(helloFrame);

        // Extract the click handler id from the rendered button. helloFrame is the JSON
        // payload — pull the html field out, then regex against the decoded HTML.
        using var helloDoc = JsonDocument.Parse(helloFrame!);
        var html = helloDoc.RootElement.GetProperty("html").GetString()!;
        var clickIdMatch = Regex.Match(html, @"data-rask-on-click=""([^""]+)""");
        Assert.True(clickIdMatch.Success, "expected data-rask-on-click in rendered HTML: " + html);
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
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await js.InvokeAsync<string>("anything", "arg").AsTask());
        Assert.Contains("Rask session", ex.Message);
    }

    private static string ExtractSessionId(string html)
    {
        var match = Regex.Match(html, "data-rask-root=\"([^\"]+)\"");
        Assert.True(match.Success, "no data-rask-root in HTML");
        return match.Groups[1].Value;
    }
}

#pragma warning disable RASK014 // private test-helper Component subclasses
internal sealed class JsRoundTripApp : Component
{
    public static TaskCompletionSource<string?> LastResult { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly IJSRuntime _js;

    public JsRoundTripApp(IJSRuntime js)
    {
        _js = js;
    }

    protected override Component Render() =>
        Fragment()[
            Doctype(),
            new Html()[new Head()[new Title()["t"]], new Body()[Text("ready")]]];

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
    public static TaskCompletionSource Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public static string? LastStatus;

    private readonly IJSRuntime _js;

    public JsClickApp(IJSRuntime js)
    {
        _js = js;
    }

    protected override Component Render() =>
        Fragment()[
            Doctype(),
            new Html()[new Head()[new Title()["t"]],
                new Body()[
                    Button(OnClickAsync: SetAsync)["set"]
                ]]];

    private async Task SetAsync()
    {
        await _js.InvokeVoidAsync("sessionStorage.setItem", "k", "v");
        LastStatus = "done";
        Completed.TrySetResult();
    }
}

internal sealed class JsErrorApp : Component
{
    public static TaskCompletionSource<Exception> Caught { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly IJSRuntime _js;

    public JsErrorApp(IJSRuntime js)
    {
        _js = js;
    }

    protected override Component Render() =>
        Fragment()[
            Doctype(),
            new Html()[new Head()[new Title()["t"]], new Body()[Text("ready")]]];

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
