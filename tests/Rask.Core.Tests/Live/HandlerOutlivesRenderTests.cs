using System.Text.Json;
using System.Text.RegularExpressions;

#pragma warning disable RASK014 // test-defined component subclasses have no generated factories

namespace Rask.Core.Tests.Live;

/// <summary>
///     Between two events of a batch a host asks whether a render made now would register the next handler
///     again. It may run ahead of the render only when the answer is no.
/// </summary>
public partial class HandlerOutlivesRenderTests : global::Rask.Core.RaskMarkup
{
    private static JsonElement EmptyPayload => JsonDocument.Parse("{}").RootElement;

    private static string IdOn(string html, string cls)
    {
        var m = Regex.Match(html, $"class=\"{cls}\"[^>]*?data-rask-on-[a-z]+=\"([^\"]+)\"");
        Assert.True(m.Success, $"no handler hook on .{cls} in: {html}");
        return m.Groups[1].Value;
    }

    private static Dictionary<Component, Component> ParentsOf(Component root)
    {
        var parents = new Dictionary<Component, Component>(ReferenceEqualityComparer.Instance);
        root.MapParents(parents);
        return parents;
    }

    private sealed class Leaf(string name) : Component
    {
        public int Clicks;

        protected override Component? Render() => Div.Class(name).OnClick(() => Clicks++)[name];
    }

    private sealed class Panel(params Component[] inside) : Component
    {
        public int Clicks;

        protected override Component? Render() => Div[Div.Class("panel").OnClick(() => Clicks++)["panel"], inside];
    }

    [Fact]
    public async Task A_handler_outlives_the_render_another_component_s_handler_asked_for()
    {
        var first = new Leaf("first");
        var second = new Leaf("second");
        var root = new StubComponent(() => Div[first, second]);
        var html = root.RenderAsLiveRoot();

        await root.TryInvokeHandlerAsync(IdOn(html, "first"), EmptyPayload);

        Assert.True(root.HandlerOutlivesRender(IdOn(html, "second"), ParentsOf(root)));
    }

    [Fact]
    public async Task A_handler_does_not_outlive_a_render_its_own_component_asked_for()
    {
        var first = new Leaf("first");
        var root = new StubComponent(() => Div[first]);
        var html = root.RenderAsLiveRoot();

        await root.TryInvokeHandlerAsync(IdOn(html, "first"), EmptyPayload);

        Assert.False(root.HandlerOutlivesRender(IdOn(html, "first"), ParentsOf(root)));
    }

    [Fact]
    public async Task A_handler_does_not_outlive_a_render_a_component_above_it_asked_for()
    {
        var leaf = new Leaf("leaf");
        var panel = new Panel(leaf);
        var root = new StubComponent(() => Div[panel]);
        var html = root.RenderAsLiveRoot();

        await root.TryInvokeHandlerAsync(IdOn(html, "panel"), EmptyPayload);

        Assert.False(root.HandlerOutlivesRender(IdOn(html, "leaf"), ParentsOf(root)));
    }

    [Fact]
    public async Task A_handler_outlives_the_render_a_component_below_it_asked_for()
    {
        var leaf = new Leaf("leaf");
        var panel = new Panel(leaf);
        var root = new StubComponent(() => Div[panel]);
        var html = root.RenderAsLiveRoot();

        await root.TryInvokeHandlerAsync(IdOn(html, "leaf"), EmptyPayload);

        Assert.True(root.HandlerOutlivesRender(IdOn(html, "panel"), ParentsOf(root)));
    }

    [Fact]
    public void An_id_the_page_does_not_have_does_not_outlive_a_render()
    {
        var root = new StubComponent(() => Div[new Leaf("leaf")]);
        root.RenderAsLiveRoot();

        var outlives = root.HandlerOutlivesRender("h-none", ParentsOf(root));

        Assert.False(outlives);
    }

    [Fact]
    public void Every_handler_outlives_a_render_nothing_asked_for()
    {
        var leaf = new Leaf("leaf");
        var panel = new Panel(leaf);
        var root = new StubComponent(() => Div[panel]);
        var html = root.RenderAsLiveRoot();

        var parents = ParentsOf(root);

        Assert.True(root.HandlerOutlivesRender(IdOn(html, "leaf"), parents));
        Assert.True(root.HandlerOutlivesRender(IdOn(html, "panel"), parents));
    }

    [Fact]
    public async Task A_handler_outlives_a_render_again_once_that_render_has_been_made()
    {
        var first = new Leaf("first");
        var root = new StubComponent(() => Div[first]);
        var html = root.RenderAsLiveRoot();
        await root.TryInvokeHandlerAsync(IdOn(html, "first"), EmptyPayload);

        html = root.RenderAsLiveRoot();

        Assert.True(root.HandlerOutlivesRender(IdOn(html, "first"), ParentsOf(root)));
    }
}
