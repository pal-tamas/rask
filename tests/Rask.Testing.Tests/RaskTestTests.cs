#pragma warning disable RASK014 // test-defined components constructed directly

namespace Rask.Testing.Tests;

public class RaskTestTests
{
    // A stateful component with a click handler — the canonical thing a consumer wants to unit-test.
    private sealed class Counter : Component
    {
        private int _count;

        protected override Component? Render() =>
            Button.Type("button").OnClick(() => _count++)[$"Count: {_count}"];
    }

    [Fact]
    public void Render_ShowsInitialMarkupAndWiresHandler()
    {
        var page = RaskTest.Render(new Counter());

        Assert.Contains("Count: 0", page.Html);
        Assert.Contains("data-rask-on-click", page.Html);
        Assert.NotNull(page.HandlerId("click"));
    }

    [Fact]
    public async Task ClickAsync_DispatchesHandlerAndReRendersWithNewState()
    {
        var page = RaskTest.Render(new Counter());
        Assert.Contains("Count: 0", page.Html);

        var afterOne = await page.ClickAsync();
        Assert.Contains("Count: 1", afterOne);

        // State persists across invocations on the same rendered component.
        await page.ClickAsync();
        Assert.Contains("Count: 2", page.Html);
    }

    [Fact]
    public async Task InvokeAsync_ByHandlerId_Works()
    {
        var page = RaskTest.Render(new Counter());
        var clickId = page.HandlerId("click")!;

        await page.InvokeAsync(clickId);

        Assert.Contains("Count: 1", page.Html);
    }

    [Fact]
    public async Task InvokeAsync_UnknownId_Throws()
    {
        var page = RaskTest.Render(new Counter());

        await Assert.ThrowsAsync<InvalidOperationException>(() => page.InvokeAsync("not-a-real-id"));
    }

    // A component that reads the event payload — proves InvokeAsync plumbs the JSON event args through.
    private sealed class NameEcho : Component
    {
        private string _name = "";

        protected override Component? Render() =>
            Div[
                Input.Value(_name).OnInput(v => _name = v),
                Span[$"Hi {_name}"]
            ];
    }

    [Fact]
    public async Task InvokeAsync_WithPayload_PassesEventArgsToHandler()
    {
        var page = RaskTest.Render(new NameEcho());
        var inputId = page.HandlerId("input")!;

        await page.InvokeAsync(inputId, "{\"value\":\"Ada\"}");

        Assert.Contains("Hi Ada", page.Html);
    }

    [Fact]
    public async Task InputAsync_ResolvesTheInputHandlerAndPassesTheValue()
    {
        var page = RaskTest.Render(new NameEcho());

        await page.InputAsync("{\"value\":\"Grace\"}");

        Assert.Contains("Hi Grace", page.Html);
    }

    [Fact]
    public async Task InvokeAsync_InvalidJson_ThrowsArgumentException()
    {
        var page = RaskTest.Render(new Counter());
        var id = page.HandlerId("click")!;

        await Assert.ThrowsAsync<ArgumentException>(() => page.InvokeAsync(id, "value=hi"));
    }

    private sealed class Labelled : Component
    {
        protected override Component? Render() =>
            Button.Type("button").Aria(new Dictionary<string, string?> { ["label"] = "Close" })["x"];
    }

    [Fact]
    public void Attr_ShortName_DoesNotMatchInsideLongerAttribute()
    {
        var page = RaskTest.Render(new Labelled());

        // "aria-label" resolves; the bare "label" must not match inside "aria-label".
        Assert.Equal("Close", page.Attr("aria-label"));
        Assert.Null(page.Attr("label"));
    }
}
