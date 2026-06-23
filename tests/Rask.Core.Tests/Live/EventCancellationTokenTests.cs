using Rask.Core;
using Rask.Core.Live;
using C = Rask.Core.Components.Generated;

#pragma warning disable RASK014 // test probe component has no generated factory

namespace Rask.Core.Tests.Live;

public class EventCancellationTokenTests
{
    [Fact]
    public void OutsideAHandler_IsTheLifetimeToken()
    {
        var c = new Probe();
        Assert.Equal(c.Lifetime, c.Event);
        Assert.False(c.Event.IsCancellationRequested);
    }

    [Fact]
    public void DuringADispatch_ReflectsThePushedDispatchToken()
    {
        var c = new Probe();
        using var cts = new CancellationTokenSource();

        using (DispatchEventTokenScope.Push(cts.Token))
        {
            Assert.Equal(cts.Token, c.Event);
            cts.Cancel();
            Assert.True(c.Event.IsCancellationRequested);
        }

        // Scope popped — back to the (un-cancelled) lifetime token.
        Assert.Equal(c.Lifetime, c.Event);
        Assert.False(c.Event.IsCancellationRequested);
    }

    private sealed class Probe : Component
    {
        public CancellationToken Lifetime => CancellationToken;
        public CancellationToken Event => EventCancellationToken;
        protected override RenderResult Render() => C.Span();
    }
}
