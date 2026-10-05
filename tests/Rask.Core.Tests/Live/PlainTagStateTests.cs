using Rask.Core.Components;
using Rask.Core.Live;

#pragma warning disable RASK014 // the tests need the very instances they hand to the render

namespace Rask.Core.Tests.Live;

// A mounted row of plain tags used to carry a ~260 B LiveState on every tag — a render handle offered to each
// one, and two lifecycle bools — which was most of what a live session retained per row. A plain tag has no
// state, lifecycle or handler of its own, so it takes none of that; everything that does keeps it.
public partial class PlainTagStateTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Plain_tags_in_a_live_render_keep_no_live_state()
    {
        var row = new TagRow();
        var page = new StubComponent(() => [row]) { RenderHandle = new NullHandle() };

        page.RenderAsLiveRoot();
        page.RenderAsLiveRoot();

        var tags = row.PersistedChildren.Values.OfType<Element>().ToList();
        Assert.NotEmpty(tags);
        Assert.All(tags, tag => Assert.False(tag.HasLiveStateInternal, tag.GetType().Name));
    }

    [Fact]
    public void A_bound_input_still_re_renders_through_the_session()
    {
        var handle = new NullHandle();
        var form = new BoundInputHost();
        var page = new StubComponent(() => [form]) { RenderHandle = handle };

        page.RenderAsLiveRoot();

        var input = Assert.Single(form.PersistedChildren.Values.OfType<HTMLInputElement<string>>());
        Assert.Same(handle, input.RenderHandle);
    }

    [Fact]
    public void A_component_built_inside_a_plain_tag_still_re_renders_through_the_session()
    {
        var handle = new NullHandle();
        var inner = new StubComponent(() => Span["inner"]);
        var page = new StubComponent(() => Div[inner]) { RenderHandle = handle };

        page.RenderAsLiveRoot();

        Assert.Same(handle, inner.RenderHandle);
    }

    [Fact]
    public void A_cached_row_lets_its_plain_tags_go_and_replays_the_same_markup()
    {
        var row = new TagRow();
        var root = new StubComponent(() => Table[Tbody[row]]);
        var cache = new SessionRenderCache();

        var first = RenderFramed(cache, root);
        var second = RenderFramed(cache, root);

        Assert.True(row.IsCleanSubtreeCachedForTest);
        Assert.False(row.RetainsElementGraphForTest);
        Assert.Equal(first, second);
    }

    [Fact]
    public void A_row_that_changes_after_its_tags_went_renders_its_new_state()
    {
        var row = new TagRow();
        var root = new StubComponent(() => Table[Tbody[row]]);
        var cache = new SessionRenderCache();
        RenderFramed(cache, root);
        RenderFramed(cache, root);

        row.Label = "changed";
        row.MarkDirtyForFrame();
        var html = RenderFramed(cache, root);

        Assert.Contains(">changed<", html, StringComparison.Ordinal);
        Assert.False(row.RetainsElementGraphForTest);
    }

    private static string RenderFramed(SessionRenderCache cache, Component root)
    {
        string html;
        using (FrameSinkScope.Push(cache.PrepareCurrentBuffer()))
        {
            html = root.RenderAsLiveRoot();
        }

        cache.TryComputeDiff([]);
        return html;
    }

    private sealed partial class TagRow : Component
    {
        public string Label { get; set; } = "a";

        protected override Component? Render() =>
            Tr.Class("line")[Td[Label], Td[Button.OnClick(() => { })["b"]]];
    }

    private sealed partial class BoundInputHost : Component
    {
        public string Name { get; set; } = "";

        protected override Component? Render() => Input.Bind(() => Name);
    }

    private sealed class NullHandle : IRenderHandle
    {
        public Task RequestRender() => Task.CompletedTask;

        public Task RequestPublishRender() => Task.CompletedTask;
    }
}
