using System.Text;
using Rask.Core.Live;

#pragma warning disable RASK014 // test-defined component subclasses have no generated factories

namespace Rask.Core.Tests.Live;

// A component whose whole render is bare text has no element of its own for the diff to address, so every
// change to it is a text node appearing, changing or leaving inside SOMEBODY ELSE'S element. Reported as "renders
// once and is never patched": the leaf was written `Text[service.Value]`, and Text dropped the words it was handed
// as children, so there was never anything to patch. Each spelling of a bare text root is pinned here.
public partial class TextRootRerenderTests : global::Rask.Core.RaskMarkup
{
    public static TheoryData<string> Spellings => ["indexer", "value", "string"];

    private static Component Bare(string spelling, string words) => spelling switch
    {
        "indexer" => Text[words],
        "value" => Text.Value(words),
        _ => words,
    };

    private static string Render(SessionRenderCache cache, Component tree, List<EditOp> ops)
    {
        var sb = new StringBuilder();
        using (FrameSinkScope.Push(cache.PrepareCurrentBuffer()))
        {
            HtmlSerializer.Serialize(tree, sb);
        }

        cache.TryComputeDiff(ops, sb.ToString());
        return sb.ToString();
    }

    // What StateHasChanged does to the leaf before the next walk reaches it.
    private static string Rerender(SessionRenderCache cache, Component tree, Component leaf, List<EditOp> ops)
    {
        leaf.MarkDirtyForFrame();
        return Render(cache, tree, ops);
    }

    [Theory]
    [MemberData(nameof(Spellings))]
    public void A_bare_text_root_that_starts_empty_is_inserted_when_it_gains_words(string spelling)
    {
        var words = string.Empty;
        var leaf = new StubComponent(() => Bare(spelling, words));
        var tree = Div.Class("step")[leaf];
        var cache = new SessionRenderCache();
        var ops = new List<EditOp>();
        var first = Render(cache, tree, ops);

        words = "Orders";
        var html = Rerender(cache, tree, leaf, ops);

        Assert.Equal("<div class=\"step\"></div>", first);
        Assert.Equal("<div class=\"step\">Orders</div>", html);
        var op = Assert.Single(ops);
        Assert.Equal(EditOpKind.InsertSubtree, op.Kind);
        Assert.Equal(new[] { 0, 0 }, op.Path);
        Assert.Equal("Orders", html[op.HtmlStart..op.HtmlEnd]);
        Assert.True(LiveDiffGate.DiffOpsAreClientSupported(ops), "the insert must ship as a diff, not fall back");
    }

    [Theory]
    [MemberData(nameof(Spellings))]
    public void A_bare_text_root_is_updated_in_place_when_its_words_change(string spelling)
    {
        var words = "Orders";
        var leaf = new StubComponent(() => Bare(spelling, words));
        var tree = Div.Class("step")[leaf];
        var cache = new SessionRenderCache();
        var ops = new List<EditOp>();
        Render(cache, tree, ops);

        words = "Invoices";
        var html = Rerender(cache, tree, leaf, ops);

        Assert.Equal("<div class=\"step\">Invoices</div>", html);
        var op = Assert.Single(ops);
        Assert.Equal(EditOpKind.UpdateText, op.Kind);
        Assert.Equal(new[] { 0, 0 }, op.Path);
        Assert.Equal("Invoices", op.Value);
    }

    [Theory]
    [MemberData(nameof(Spellings))]
    public void A_bare_text_root_is_removed_when_its_words_are_cleared(string spelling)
    {
        var words = "Orders";
        var leaf = new StubComponent(() => Bare(spelling, words));
        var tree = Div.Class("step")[leaf];
        var cache = new SessionRenderCache();
        var ops = new List<EditOp>();
        Render(cache, tree, ops);

        words = string.Empty;
        var html = Rerender(cache, tree, leaf, ops);

        Assert.Equal("<div class=\"step\"></div>", html);
        var op = Assert.Single(ops);
        Assert.Equal(EditOpKind.RemoveSubtree, op.Kind);
        Assert.Equal(new[] { 0, 0 }, op.Path);
        Assert.True(LiveDiffGate.DiffOpsAreClientSupported(ops), "the removal must ship as a diff, not fall back");
    }

    [Theory]
    [MemberData(nameof(Spellings))]
    public void A_bare_text_root_between_sibling_elements_is_updated_at_its_own_slot(string spelling)
    {
        var words = "Orders";
        var leaf = new StubComponent(() => Bare(spelling, words));
        var tree = Div[Span["before"], leaf, Span["after"]];
        var cache = new SessionRenderCache();
        var ops = new List<EditOp>();
        Render(cache, tree, ops);

        words = "Invoices";
        var html = Rerender(cache, tree, leaf, ops);

        Assert.Equal("<div><span>before</span>Invoices<span>after</span></div>", html);
        var op = Assert.Single(ops);
        Assert.Equal(EditOpKind.UpdateText, op.Kind);
        Assert.Equal(new[] { 0, 1 }, op.Path);
    }

    [Theory]
    [MemberData(nameof(Spellings))]
    public void A_bare_text_root_between_sibling_elements_reaches_the_page_when_it_gains_words(string spelling)
    {
        var words = string.Empty;
        var leaf = new StubComponent(() => Bare(spelling, words));
        var tree = Div[Span["before"], leaf, Span["after"]];
        var cache = new SessionRenderCache();
        var ops = new List<EditOp>();
        Render(cache, tree, ops);

        words = "Orders";
        var html = Rerender(cache, tree, leaf, ops);

        // A node appearing mid-list is one run among siblings that still pair up: an insert at its slot.
        Assert.Equal("<div><span>before</span>Orders<span>after</span></div>", html);
        var op = Assert.Single(ops);
        Assert.Equal(EditOpKind.InsertSubtree, op.Kind);
        Assert.Equal(new[] { 0, 1 }, op.Path);
        Assert.True(LiveDiffGate.DiffOpsAreClientSupported(ops));
    }

    [Theory]
    [MemberData(nameof(Spellings))]
    public void A_bare_text_root_handed_to_another_component_as_its_child_is_patched(string spelling)
    {
        var words = string.Empty;
        var leaf = new StubComponent(() => Bare(spelling, words));
        var item = new ItemWithChildren { Children = [leaf] };
        var cache = new SessionRenderCache();
        var ops = new List<EditOp>();
        Render(cache, item, ops);

        words = "Orders";
        var inserted = Rerender(cache, item, leaf, ops);
        var insert = Assert.Single(ops);
        words = "Invoices";
        var updated = Rerender(cache, item, leaf, ops);
        var update = Assert.Single(ops);

        Assert.Equal("<div class=\"item\"><div class=\"step\">Orders</div><span>&gt;</span></div>", inserted);
        Assert.Equal(EditOpKind.InsertSubtree, insert.Kind);
        Assert.Equal(new[] { 0, 0, 0 }, insert.Path);
        Assert.Equal("<div class=\"item\"><div class=\"step\">Invoices</div><span>&gt;</span></div>", updated);
        Assert.Equal(EditOpKind.UpdateText, update.Kind);
        Assert.Equal(new[] { 0, 0, 0 }, update.Path);
    }

    [Fact]
    public void A_fragment_root_is_patched_when_one_of_its_texts_changes()
    {
        var words = "Orders";
        var leaf = new StubComponent(() => Fragment[Em["in "], words]);
        var tree = Div[leaf];
        var cache = new SessionRenderCache();
        var ops = new List<EditOp>();
        Render(cache, tree, ops);

        words = "Invoices";
        var html = Rerender(cache, tree, leaf, ops);

        Assert.Equal("<div><em>in </em>Invoices</div>", html);
        var op = Assert.Single(ops);
        Assert.Equal(EditOpKind.UpdateText, op.Kind);
        Assert.Equal(new[] { 0, 1 }, op.Path);
        Assert.Equal("Invoices", op.Value);
    }

    [Theory]
    [MemberData(nameof(Spellings))]
    public void A_root_that_rendered_nothing_is_inserted_when_it_becomes_text(string spelling)
    {
        string? words = null;
        var leaf = new NullableRoot(() => words is null ? null : Bare(spelling, words));
        var tree = Div[leaf];
        var cache = new SessionRenderCache();
        var ops = new List<EditOp>();
        var first = Render(cache, tree, ops);

        words = "Orders";
        var html = Rerender(cache, tree, leaf, ops);

        Assert.Equal("<div></div>", first);
        Assert.Equal("<div>Orders</div>", html);
        var op = Assert.Single(ops);
        Assert.Equal(EditOpKind.InsertSubtree, op.Kind);
        Assert.Equal(new[] { 0, 0 }, op.Path);
        Assert.Equal("Orders", html[op.HtmlStart..op.HtmlEnd]);
    }

    [Fact]
    public void A_bare_text_root_that_did_not_change_ships_nothing()
    {
        var leaf = new StubComponent(() => Text["Orders"]);
        var tree = Div[leaf];
        var cache = new SessionRenderCache();
        var ops = new List<EditOp>();
        Render(cache, tree, ops);

        var html = Rerender(cache, tree, leaf, ops);

        Assert.Equal("<div>Orders</div>", html);
        Assert.Empty(ops);
    }

    private sealed partial class ItemWithChildren : Component
    {
        protected override Component? Render() =>
            Div.Class("item")[Div.Class("step")[Children ?? []], Span[">"]];
    }

    private sealed partial class NullableRoot(Func<Component?> render) : Component
    {
        protected override Component? Render() => render();
    }
}
