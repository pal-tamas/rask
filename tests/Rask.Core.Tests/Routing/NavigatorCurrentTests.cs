using Rask.Core.Routing;

namespace Rask.Core.Tests.Routing;

/// <summary>
///     The ambient <see cref="Navigator.Current" /> that the generated <c>SomePage.Go(...)</c> helpers
///     navigate through. It is published for exactly the window in which navigation is legal — the handler
///     scope every host enters around a dispatch — so a static helper with no receiver can still reach the
///     right session's navigator.
/// </summary>
public class NavigatorCurrentTests
{
    private static Navigator Build(string path = "/") => new(new RouteState { Path = path });

    [Fact]
    public void Current_IsNull_OutsideAHandler()
    {
        _ = Build();
        Assert.Null(Navigator.Current);
    }

    [Fact]
    public void Current_IsTheNavigator_InsideAHandler()
    {
        var nav = Build();
        using (nav.EnterHandler())
        {
            Assert.Same(nav, Navigator.Current);
        }
    }

    [Fact]
    public void Current_IsClearedWhenTheHandlerScopeEnds()
    {
        var nav = Build();
        using (nav.EnterHandler())
        {
        }

        Assert.Null(Navigator.Current);
    }

    [Fact]
    public void RequireCurrent_OutsideAHandler_ThrowsTheActionableMessage()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Navigator.RequireCurrent());

        // Same guidance the instance methods give, so `SomePage.Go()` misused during Render() reports
        // the real problem rather than a NullReferenceException.
        Assert.Contains("event handlers", ex.Message);
    }

    [Fact]
    public void RequireCurrent_InsideAHandler_Navigates()
    {
        var state = new RouteState { Path = "/" };
        var nav = new Navigator(state);

        using (nav.EnterHandler())
        {
            // Exactly what a generated `SomePage.Go(...)` body does.
            Navigator.RequireCurrent().NavigateTo(new RouteUrl("/products/42", "?sort=asc"));
        }

        Assert.Equal("/products/42", state.Path);
        Assert.Equal("asc", state.Query["sort"]);
    }

    [Fact]
    public void NestedScopes_RestoreTheOuterNavigator()
    {
        // The Server dispatch nests scopes (navigator + authSignIn), and tests re-enter, so unwinding
        // has to restore rather than clear.
        var outer = Build();
        var inner = Build();

        using (outer.EnterHandler())
        {
            using (inner.EnterHandler())
            {
                Assert.Same(inner, Navigator.Current);
            }

            Assert.Same(outer, Navigator.Current);
        }

        Assert.Null(Navigator.Current);
    }

    [Fact]
    public async Task Current_FlowsAcrossAnAwait()
    {
        // A handler may await; AsyncLocal is what carries the ambient navigator into the continuation,
        // which may resume on a different pool thread.
        var state = new RouteState { Path = "/" };
        var nav = new Navigator(state);

        using (nav.EnterHandler())
        {
            await Task.Yield();
            Assert.Same(nav, Navigator.Current);
            Navigator.RequireCurrent().NavigateTo("/after-await");
        }

        Assert.Equal("/after-await", state.Path);
    }
}
