using Rask.Core.Live;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Live;

// Covers the handler-dispatch switch `default:` arm in Component.TryInvokeHandlerAsync — the path
// taken by a delegate whose signature isn't in the fast-path list (realistically a drag handler
// assigned through the untyped Element.OnDrag* `Delegate?` slot, e.g. a method group typed
// Func<Task<T>>). The arm must await a returned awaitable so faults route to the ErrorBoundary
// and post-await state changes still render — not fire-and-forget it.
public partial class DefaultHandlerAsyncTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public async Task UnmatchedAsyncSignature_IsAwaited_BeforeDispatchReturns()
    {
        var sp = RenderHarness.EmptyServices();
        var owner = new UnmatchedAsyncOwner(false);

        using var ctx = LiveRenderContext.Begin(owner, sp);
        _ = owner.ToHtml();

        var handlerId = owner.RegisteredHandlerId
                        ?? throw new InvalidOperationException("handler never registered");

        await owner.TryInvokeHandlerAsync(handlerId, default);

        // The continuation after `await Task.Yield()` only runs if the dispatcher awaited the
        // returned Task. Fire-and-forget would let TryInvokeHandlerAsync return first, leaving
        // this false.
        Assert.True(owner.ContinuationRan, "dispatcher must await the returned Task");
    }

    [Fact]
    public async Task UnmatchedAsyncSignature_Throw_TripsAncestorBoundary()
    {
        var sp = RenderHarness.EmptyServices();
        var owner = new UnmatchedAsyncOwner(true);
        var boundary = ErrorBoundary.Value;
        boundary.SetProps(new Component[] { owner }, null);

        using var ctx = LiveRenderContext.Begin(boundary, sp);
        _ = boundary.ToHtml();

        var handlerId = owner.RegisteredHandlerId
                        ?? throw new InvalidOperationException("handler never registered");

        var invoked = await boundary.TryInvokeHandlerAsync(handlerId, default);

        Assert.True(invoked);
        Assert.NotNull(boundary.Error);
        Assert.Equal("default-async", boundary.Error!.Message);
    }

    private sealed class UnmatchedAsyncOwner : Component
    {
        private readonly bool _throws;

        public UnmatchedAsyncOwner(bool throws) => _throws = throws;
        public string? RegisteredHandlerId { get; private set; }
        public bool ContinuationRan { get; private set; }

        protected override Component? Render()
        {
            var live = LiveRenderContext.Current!;
            // Func<Task<bool>> is NOT in the dispatch fast-path (only Func<Task> is), so it lands
            // on the `default:` arm — the realistic shape of a drag handler bound via Delegate?.
            Func<Task<bool>> handler = async () =>
            {
                await Task.Yield();
                if (_throws)
                {
                    throw new InvalidOperationException("default-async");
                }

                ContinuationRan = true;
                return true;
            };

            RegisteredHandlerId = live.RegisterHandler(handler);
            return Span[Text.Value("owner")];
        }
    }
}
