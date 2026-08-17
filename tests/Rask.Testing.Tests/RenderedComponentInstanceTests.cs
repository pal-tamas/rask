#pragma warning disable RASK014 // test-defined components constructed directly

namespace Rask.Testing.Tests;

// Markup is not always the thing under test: a component's own state is often what a test wants to assert.
// These pin Instance's identity guarantee and the non-throwing dispatch.
public partial class RenderedComponentInstanceTests : global::Rask.Core.RaskMarkup
{
    private sealed class Counter : Component
    {
        public int Count { get; private set; }

        protected override Component? Render() =>
            Button.Type("button").OnClick(() => Count++)[$"Count: {Count}"];
    }

    [Fact]
    public void Instance_IsTheObjectPassedIn()
    {
        var counter = new Counter();
        var page = RaskTest.Render(counter);

        Assert.Same(counter, page.Instance);
    }

    [Fact]
    public async Task Instance_StaysTheSameObjectAcrossRenders_AndExposesState()
    {
        var counter = new Counter();
        var page = RaskTest.Render(counter);

        await page.ClickAsync();
        await page.ClickAsync();
        page.Render();

        // The identity guarantee: the forwarding root renders the caller's object rather than reconciling
        // it, so a test can hold on to Instance and assert state directly instead of parsing it back out.
        Assert.Same(counter, page.Instance);
        Assert.Equal(2, page.Instance.Count);
    }

    // A component that stops wiring its handler once clicked — the shape TryInvokeAsync exists for.
    private sealed class OneShot : Component
    {
        private bool _done;

        protected override Component? Render() =>
            _done ? Span["done"] : Button.Type("button").OnClick(() => _done = true)["go"];
    }

    [Fact]
    public async Task TryInvokeAsync_LiveHandler_DispatchesAndReports()
    {
        var page = RaskTest.Render(new OneShot());
        var id = page.HandlerId("click")!;

        Assert.True(await page.TryInvokeAsync(id));
        Assert.Contains("done", page.Html);
    }

    [Fact]
    public async Task TryInvokeAsync_HandlerThatIsGone_ReturnsFalseInsteadOfThrowing()
    {
        var page = RaskTest.Render(new OneShot());
        var id = page.HandlerId("click")!;
        await page.InvokeAsync(id);

        // The button is gone, so its id is no longer live. InvokeAsync would throw here; the point of
        // TryInvokeAsync is to let a test assert the handler is gone.
        Assert.False(await page.TryInvokeAsync(id));
        Assert.Empty(page.HandlerIds("click"));
    }

    private sealed class RenderCount : Component
    {
        public int Renders { get; private set; }

        protected override Component? Render()
        {
            Renders++;
            return Span[$"{Renders}"];
        }
    }

    [Fact]
    public async Task TryInvokeAsync_DeadHandler_DoesNotReRender()
    {
        var page = RaskTest.Render(new RenderCount());
        Assert.Equal(1, page.Instance.Renders);

        Assert.False(await page.TryInvokeAsync("not-a-real-id"));

        // Nothing was dispatched, so nothing can have changed — re-rendering would be pure noise.
        Assert.Equal(1, page.Instance.Renders);
    }

    [Fact]
    public async Task TryInvokeAsync_InvalidJson_StillThrows()
    {
        var page = RaskTest.Render(new Counter());
        var id = page.HandlerId("click")!;

        // A malformed payload is a bug in the test, not a "handler missing" condition, so it throws either way.
        await Assert.ThrowsAsync<ArgumentException>(() => page.TryInvokeAsync(id, "value=hi"));
    }
}
