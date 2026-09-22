#pragma warning disable RASK014 // test-defined components constructed directly

namespace Rask.Testing.Tests;

public partial class RaskTestTests : global::Rask.Core.RaskMarkup
{
    // A stateful component with a click handler — the canonical thing a consumer wants to unit-test.
    private sealed class Counter : Component
    {
        private int _count;

        protected override Component? Render() =>
            Button.Type("button").OnClick(() => _count++)[$"Count: {_count}"];
    }

    [Fact]
    public void Rendering_shows_the_initial_markup_and_wires_the_handler()
    {
        var page = Test.Render(new Counter());

        Assert.Contains("Count: 0", page.Html);
        Assert.Contains("data-rask-on-click", page.Html);
        Assert.NotNull(page.HandlerId("click"));
    }

    [Fact]
    public async Task A_click_dispatches_the_handler_and_rerenders_with_the_new_state()
    {
        var page = Test.Render(new Counter());

        Assert.Contains("Count: 0", page.Html);

        var afterOne = await page.ClickAsync();

        Assert.Contains("Count: 1", afterOne);

        // State persists across invocations on the same rendered component.
        await page.ClickAsync();

        Assert.Contains("Count: 2", page.Html);
    }

    [Fact]
    public async Task InvokeAsync_by_handler_id_runs_the_handler()
    {
        var page = Test.Render(new Counter());
        var clickId = page.HandlerId("click")!;

        await page.InvokeAsync(clickId);

        Assert.Contains("Count: 1", page.Html);
    }

    [Fact]
    public async Task InvokeAsync_throws_on_an_unknown_id()
    {
        var page = Test.Render(new Counter());

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
    public async Task InvokeAsync_with_a_payload_passes_the_event_args_to_the_handler()
    {
        var page = Test.Render(new NameEcho());
        var inputId = page.HandlerId("input")!;

        await page.InvokeAsync(inputId, "{\"value\":\"Ada\"}");

        Assert.Contains("Hi Ada", page.Html);
    }

    [Fact]
    public async Task InputAsync_resolves_the_input_handler_and_passes_the_value()
    {
        var page = Test.Render(new NameEcho());

        await page.InputAsync("{\"value\":\"Grace\"}");

        Assert.Contains("Hi Grace", page.Html);
    }

    [Fact]
    public async Task InvokeAsync_with_invalid_json_throws_an_ArgumentException()
    {
        var page = Test.Render(new Counter());
        var id = page.HandlerId("click")!;

        await Assert.ThrowsAsync<ArgumentException>(() => page.InvokeAsync(id, "value=hi"));
    }

    private sealed class Labelled : Component
    {
        protected override Component? Render() =>
            Button.Type("button").Aria(new Dictionary<string, string?> { ["label"] = "Close" })["x"];
    }

    [Fact]
    public void A_short_attribute_name_does_not_match_inside_a_longer_attribute()
    {
        var page = Test.Render(new Labelled());

        // "aria-label" resolves; the bare "label" must not match inside "aria-label".
        Assert.Equal("Close", page.Attr("aria-label"));
        Assert.Null(page.Attr("label"));
    }
}
