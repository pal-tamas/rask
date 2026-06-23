using Rask.Core;
using Rask.Core.Live;
using C = Rask.Core.Components.Generated;

#pragma warning disable RASK014 // test probe component has no generated factory

namespace Rask.Core.Tests.Live;

// Component.CancellationToken is the lifetime token by default, but reflects the per-dispatch token an
// event handler runs under (so a handler timeout / socket close cancels the handler's async work).
public class CancellationTokenScopeTests
{
    [Fact]
    public void OutsideAHandler_IsAStableLifetimeToken()
    {
        var c = new Probe();
        var a = c.Token;
        var b = c.Token;

        Assert.Equal(a, b); // same lifetime token each read
        Assert.False(a.IsCancellationRequested);
    }

    [Fact]
    public void DuringADispatch_ReflectsThePushedDispatchToken()
    {
        var c = new Probe();
        using var cts = new CancellationTokenSource();

        using (DispatchEventTokenScope.Push(cts.Token))
        {
            Assert.Equal(cts.Token, c.Token);
            cts.Cancel();
            Assert.True(c.Token.IsCancellationRequested);
        }

        // Scope popped — back to the (un-cancelled) lifetime token.
        Assert.False(c.Token.IsCancellationRequested);
    }

    private sealed class Probe : Component
    {
        public CancellationToken Token => CancellationToken;
        protected override RenderResult Render() => C.Span();
    }
}
