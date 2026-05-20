using System.Text.Json;
using Rask.Core.Live;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Live;

public class AsyncHandlerRenderingTests
{
    private static JsonElement EmptyPayload => JsonDocument.Parse("{}").RootElement;

    [Fact]
    public async Task AsyncHandler_RendersBeforeAndAfterEachAwait()
    {
        var state = "init";
        var handle = new RecordingRenderHandle(() => state);
        var component = new StubComponent(Span()) { RenderHandle = handle };

        component.RegisterTestHandler("h0", new Func<Task>(async () =>
        {
            state = "A";
            await Task.Yield();
            state = "B";
            await Task.Yield();
            state = "C";
        }));

        await component.TryInvokeHandlerAsync("h0", EmptyPayload);

        Assert.Contains("A", handle.Snapshots);
        Assert.Contains("B", handle.Snapshots);
        Assert.Contains("C", handle.Snapshots);

        var firstA = handle.Snapshots.IndexOf("A");
        var firstB = handle.Snapshots.IndexOf("B");
        var firstC = handle.Snapshots.IndexOf("C");
        Assert.True(firstA < firstB && firstB < firstC,
            $"expected progression A→B→C, got: [{string.Join(",", handle.Snapshots)}]");
    }

    [Fact]
    public async Task AsyncHandler_StringPayload_AlsoRendersProgressively()
    {
        var state = "init";
        var handle = new RecordingRenderHandle(() => state);
        var component = new StubComponent(Span()) { RenderHandle = handle };

        component.RegisterTestHandler("h0", new Func<string, Task>(async value =>
        {
            state = "got:" + value;
            await Task.Yield();
            state = "done:" + value;
        }));

        var payload = JsonDocument.Parse("{\"value\":\"hi\"}").RootElement;
        await component.TryInvokeHandlerAsync("h0", payload);

        Assert.Contains("got:hi", handle.Snapshots);
        Assert.Contains("done:hi", handle.Snapshots);
    }

    [Fact]
    public async Task SyncActionHandler_DoesNotInvokeRenderInScope()
    {
        var handle = new RecordingRenderHandle(() => "");
        var component = new StubComponent(Span()) { RenderHandle = handle };

        var ran = false;
        component.RegisterTestHandler("h0", new Action(() => ran = true));

        await component.TryInvokeHandlerAsync("h0", EmptyPayload);

        Assert.True(ran);
        Assert.Empty(handle.Snapshots);
    }

    [Fact]
    public async Task AsyncHandler_NoAwaits_DoesNotInvokeRenderInScope()
    {
        var handle = new RecordingRenderHandle(() => "");
        var component = new StubComponent(Span()) { RenderHandle = handle };

        component.RegisterTestHandler("h0", new Func<Task>(() => Task.CompletedTask));

        await component.TryInvokeHandlerAsync("h0", EmptyPayload);

        Assert.Empty(handle.Snapshots);
    }

    [Fact]
    public async Task AsyncHandler_NoRenderHandle_DoesNotThrow()
    {
        var component = new StubComponent(Span());
        Assert.Null(component.RenderHandle);

        component.RegisterTestHandler("h0", new Func<Task>(async () => await Task.Yield()));

        await component.TryInvokeHandlerAsync("h0", EmptyPayload);
    }

    [Fact]
    public async Task AsyncHandler_DoesNotLeaveHandlerSyncContextInstalled()
    {
        var handle = new RecordingRenderHandle(() => "");
        var component = new StubComponent(Span()) { RenderHandle = handle };

        component.RegisterTestHandler("h0", new Func<Task>(async () => await Task.Yield()));

        await component.TryInvokeHandlerAsync("h0", EmptyPayload);

        Assert.IsNotType<HandlerSyncContext>(SynchronizationContext.Current);
    }

    private sealed class RecordingRenderHandle : IRenderHandle
    {
        private readonly Func<string> _snapshot;

        public RecordingRenderHandle(Func<string> snapshot) => _snapshot = snapshot;
        public List<string> Snapshots { get; } = new();

        public Task RequestRenderAsync() => Task.CompletedTask;

        Task IRenderHandle.RenderInScopeAsync()
        {
            lock (Snapshots)
            {
                Snapshots.Add(_snapshot());
            }

            return Task.CompletedTask;
        }
    }
}

internal static class StubComponentTestExtensions
{
    public static void RegisterTestHandler(this Component component, string id, Delegate handler)
    {
        // RegisterHandler issues sequential ids starting at h0; the first call returns "h0".
        var actual = component.RegisterHandler(handler);
        if (actual != id)
        {
            throw new InvalidOperationException($"expected handler id {id}, got {actual}");
        }
    }
}
