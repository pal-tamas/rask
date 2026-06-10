using Rask.Core.Live;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Live;

public class EventHandlerErrorBoundaryTests
{
    [Fact]
    public async Task SyncHandlerThrow_TripsAncestorBoundary()
    {
        var sp = RenderHarness.EmptyServices();
        var handlerOwner = new HandlerOwner(true);
        var boundary = ErrorBoundary();
        boundary.SetProps(new Child[] { handlerOwner }, null);

        // ToHtml stamps handlerOwner.Boundary AND registers the handler under the live
        // root's handler dict. RegisterHandler happens during the serialization walk via
        // RenderForLive -> Render -> handler creation.
        using var ctx = LiveRenderContext.Begin(boundary, sp);
        _ = boundary.ToHtml();

        // The root is the boundary, so its _handlers got populated during the walk.
        var handlerId = handlerOwner.RegisteredHandlerId
                        ?? throw new InvalidOperationException("handler never registered");
        var invoked = await boundary.TryInvokeHandlerAsync(handlerId, default);

        Assert.True(invoked, "handler should have been found and consumed");
        Assert.NotNull(boundary.Error);
        Assert.Equal("handler-sync", boundary.Error!.Message);
    }

    [Fact]
    public async Task AsyncHandlerThrow_TripsAncestorBoundary()
    {
        var sp = RenderHarness.EmptyServices();
        var handlerOwner = new HandlerOwner(false);
        var boundary = ErrorBoundary();
        boundary.SetProps(new Child[] { handlerOwner }, null);

        using var ctx = LiveRenderContext.Begin(boundary, sp);
        _ = boundary.ToHtml();

        var handlerId = handlerOwner.RegisteredHandlerId
                        ?? throw new InvalidOperationException("handler never registered");
        var invoked = await boundary.TryInvokeHandlerAsync(handlerId, default);

        Assert.True(invoked);
        Assert.NotNull(boundary.Error);
        Assert.Equal("handler-async", boundary.Error!.Message);
    }

    [Fact]
    public async Task HandlerThrow_NoBoundary_BubblesOut()
    {
        // When the handler's owner has no Boundary, TryInvokeHandlerAsync re-throws so
        // the dispatcher (server/WASM) can apply its own catch-and-log fallback.
        var sp = RenderHarness.EmptyServices();
        var owner = new HandlerOwner(true);

        using var ctx = LiveRenderContext.Begin(owner, sp);
        _ = owner.ToHtml();

        var handlerId = owner.RegisteredHandlerId
                        ?? throw new InvalidOperationException("handler never registered");

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await owner.TryInvokeHandlerAsync(handlerId, default));
    }

    private sealed class HandlerOwner : Component
    {
        private readonly bool _throwsSync;

        public HandlerOwner(bool throwsSync) => _throwsSync = throwsSync;
        public string? RegisteredHandlerId { get; private set; }

        protected override RenderResult Render()
        {
            // Register the handler against the live render context so the live root holds
            // it in _handlers and TryInvokeHandlerAsync can look it up by id.
            Action<Component> bind = _ => { };
            var ctx = LiveRenderContext.Current!;
            if (_throwsSync)
            {
                RegisteredHandlerId = ctx.RegisterHandler((Action)(() =>
                    throw new InvalidOperationException("handler-sync")));
            }
            else
            {
                RegisteredHandlerId = ctx.RegisterHandler((Func<Task>)(() =>
                    throw new InvalidOperationException("handler-async")));
            }

            return Span()[Text("owner")];
        }
    }
}
