using System.Text;
using Rask.Core.Live;

#pragma warning disable RASK014 // the opaque host is a test-defined component with no generated entry
#pragma warning disable RASK022 // rows nothing keys, and a keyed child beside unkeyed ones, are the subject here

namespace Rask.Core.Tests.Live;

// A level where one child took the PLACE of another — `cond ? A : B`, words that become an element — used
// to be answered with the whole page. Now its PARENT travels alone: one trusted MorphSubtree, where the
// level's own ops were, and nothing outside that parent is named at all.
//
// Every reply here is also REPLAYED (FrameDom, which pairs a morph's children as rask-morph.ts does): the
// ops, applied in the order they were written, must leave the document the new render describes, and a node
// outside the morphed parent must be the node it was.
public partial class RefusedLevelMorphTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void An_element_in_place_of_another_is_one_morph_of_its_parent_and_nothing_outside_it_is_touched()
    {
        var reply = Reply.To(
            Div[Header[Input.Value("").Name("search")], Main[H1["Orders"], Span["loading"]]],
            Div[Header[Input.Value("").Name("search")], Main[H1["Orders"], Ul[Li["a"]]]]);
        var search = reply.Before.At(0, 0, 0);
        var heading = reply.Before.At(0, 1, 0);

        reply.Apply();

        var morph = Assert.Single(reply.Ops);
        Assert.Equal(EditOpKind.MorphSubtree, morph.Kind);
        Assert.Equal([0, 1], morph.Path);
        Assert.True(morph.Trusted);
        Assert.Equal("<h1>Orders</h1><ul><li>a</li></ul>", Assert.Single(reply.Morphed));
        Assert.Same(search, reply.Before.At(0, 0, 0));
        Assert.Same(heading, reply.Before.At(0, 1, 0));
        Assert.Equal(reply.Expected, reply.Before.Html);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Words_and_an_element_in_each_others_place_are_one_morph_of_their_parent(bool wordsFirst)
    {
        Component Slot(bool words) => Div[H1["Orders"], P[B["3"], words ? " open" : Em["open"]]];
        var reply = Reply.To(Slot(wordsFirst), Slot(!wordsFirst));
        var count = reply.Before.At(0, 1, 0);

        reply.Apply();

        var morph = Assert.Single(reply.Ops);
        Assert.Equal(EditOpKind.MorphSubtree, morph.Kind);
        Assert.Equal([0, 1], morph.Path);
        Assert.Same(count, reply.Before.At(0, 1, 0));
        Assert.Equal(reply.Expected, reply.Before.Html);
    }

    [Fact]
    public void Two_children_arriving_apart_from_each_other_are_one_morph_of_their_parent()
    {
        var reply = Reply.To(
            Div[Div[H1["Orders"], Ul[Li["a"]]]],
            Div[Div[Aside["Saved"], H1["Orders"], P["Nothing else to do."], Ul[Li["a"]]]]);

        reply.Apply();

        var morph = Assert.Single(reply.Ops);
        Assert.Equal(EditOpKind.MorphSubtree, morph.Kind);
        Assert.Equal([0, 0], morph.Path);
        Assert.Equal(reply.Expected, reply.Before.Html);
    }

    [Fact]
    public void A_child_arriving_while_a_sibling_changes_element_is_one_morph_of_their_parent()
    {
        var reply = Reply.To(
            Div[Div[H1["Orders"], Span["a"], Ul[Li["a"]]]],
            Div[Div[H1["Orders"], Aside["Saved"], Em["a"], Ul[Li["a"]]]]);
        var heading = reply.Before.At(0, 0, 0);

        reply.Apply();

        Assert.Equal(EditOpKind.MorphSubtree, Assert.Single(reply.Ops).Kind);
        Assert.Same(heading, reply.Before.At(0, 0, 0));
        Assert.Equal(reply.Expected, reply.Before.Html);
    }

    [Fact]
    public void A_replacement_at_two_depths_at_once_is_one_morph_of_the_outer_parent()
    {
        var reply = Reply.To(
            Div[Section[Span["Open"], Div[H3["Lines"], P["Loading"]]]],
            Div[Section[Em["Shipped"], Div[H3["Lines"], Ul[Li["a"]]]]]);

        reply.Apply();

        var morph = Assert.Single(reply.Ops);
        Assert.Equal([0, 0], morph.Path);
        Assert.Equal(reply.Expected, reply.Before.Html);
    }

    [Fact]
    public void A_replacement_below_a_level_that_is_patched_node_by_node_stays_a_morph_of_the_inner_parent()
    {
        var reply = Reply.To(
            Div[Section[H3["1 line"], Div[Span["Loading"], P["after"]]]],
            Div[Section[H3["2 lines"], Div[Ul[Li["a"]], P["after"]]]]);

        reply.Apply();

        Assert.Equal(
            [(EditOpKind.UpdateText, "0/0/0/0"), (EditOpKind.MorphSubtree, "0/0/1")],
            reply.Ops.Select(op => (op.Kind, string.Join('/', op.Path))));
        Assert.Equal(reply.Expected, reply.Before.Html);
    }

    [Fact]
    public void The_morph_sits_where_its_levels_ops_were_between_the_ops_of_the_siblings_around_it()
    {
        var reply = Reply.To(
            Div[P.Class("idle")["before"], Section[Span["a"], B["kept"]], P["1 open"]],
            Div[P.Class("busy")["before"], Section[Em["a"], B["kept"]], P["2 open"]]);
        var kept = reply.Before.At(0, 1, 1);

        reply.Apply();

        Assert.Equal(
            [(EditOpKind.SetAttribute, "0/0"), (EditOpKind.MorphSubtree, "0/1"), (EditOpKind.UpdateText, "0/2/0")],
            reply.Ops.Select(op => (op.Kind, string.Join('/', op.Path))));
        Assert.Same(kept, reply.Before.At(0, 1, 1));
        Assert.Equal(reply.Expected, reply.Before.Html);
    }

    [Fact]
    public void The_parents_own_attributes_are_patched_before_its_children_are_morphed()
    {
        var reply = Reply.To(
            Div[Section.Class("loading")[Span["Loading"], P["after"]]],
            Div[Section.Class("ready")[Ul[Li["a"]], P["after"]]]);
        var section = reply.Before.At(0, 0);

        reply.Apply();

        Assert.Equal(
            [(EditOpKind.SetAttribute, "0/0"), (EditOpKind.MorphSubtree, "0/0")],
            reply.Ops.Select(op => (op.Kind, string.Join('/', op.Path))));
        Assert.Same(section, reply.Before.At(0, 0));
        Assert.Equal(reply.Expected, reply.Before.Html);
    }

    [Fact]
    public void A_morph_after_a_child_that_arrived_above_names_its_parent_at_the_slot_it_has_once_the_child_is_in()
    {
        var reply = Reply.To(
            Div[H1["Orders"], Section[Span["Loading"], P["after"]]],
            Div[H1["Orders"], Aside["Saved"], Section[Ul[Li["a"]], P["after"]]]);
        var section = reply.Before.At(0, 1);

        reply.Apply();

        Assert.Equal(
            [(EditOpKind.InsertSubtree, "0/1"), (EditOpKind.MorphSubtree, "0/2")],
            reply.Ops.Select(op => (op.Kind, string.Join('/', op.Path))));
        Assert.Same(section, reply.Before.At(0, 2));
        Assert.Equal(reply.Expected, reply.Before.Html);
    }

    [Fact]
    public void A_morph_inside_a_row_of_a_keyed_list_that_was_reordered_names_the_row_at_its_new_place()
    {
        var reply = Reply.To(
            Ul[Li.Key("a")[Span["Alpha"], B["a"]], Li.Key("b")[Span["Beta"], B["b"]], Li.Key("c")[Span["Gamma"], B["c"]]],
            Ul[Li.Key("c")[Span["Gamma"], B["c"]], Li.Key("a")[Span["Alpha"], B["a"]], Li.Key("b")[Em["Beta"], B["b"]]]);

        var kinds = reply.Ops.Select(op => (op.Kind, string.Join('/', op.Path)));

        Assert.Equal([(EditOpKind.PermutationBatch, "0"), (EditOpKind.MorphSubtree, "0/2")], kinds);
        Assert.Equal("<em>Beta</em><b>b</b>", Assert.Single(reply.Morphed));
        Assert.True(LiveDiffGate.DiffOpsAreClientSupported(reply.Ops));
    }

    [Fact]
    public void A_keyed_child_inside_the_morphed_parent_stays_the_node_it_was_wherever_it_lands()
    {
        var reply = Reply.To(
            Div[Div[Span["Loading"], Fieldset.Key("form")[Input.Value("a").Name("name")]]],
            Div[Div[Aside["Saved"], Ul[Li["a"]], Fieldset.Key("form")[Input.Value("a").Name("name")]]]);
        var field = reply.Before.At(0, 0, 1, 0);

        reply.Apply();

        Assert.Equal(EditOpKind.MorphSubtree, Assert.Single(reply.Ops).Kind);
        Assert.Same(field, reply.Before.At(0, 0, 2, 0));
        Assert.Equal(reply.Expected, reply.Before.Html);
    }

    [Fact]
    public void An_element_in_place_of_another_directly_under_the_body_is_still_answered_with_the_whole_page()
    {
        var reply = Reply.To(
            Body[Nav[A.Href("/")["Home"]], Span["loading"], P["after"]],
            Body[Nav[A.Href("/")["Home"]], Ul[Li["a"]], P["after"]]);

        var kinds = reply.Ops.Select(op => op.Kind);

        Assert.Equal([EditOpKind.RemoveSubtree, EditOpKind.InsertSubtree], kinds);
        Assert.Equal(ReplyKind.Page, reply.Kind);
    }

    [Fact]
    public void An_element_in_place_of_another_at_the_documents_own_level_is_still_answered_with_the_whole_page()
    {
        var reply = Reply.To([Span["loading"], P["after"]], [Ul[Li["a"]], P["after"]]);

        var kind = reply.Kind;

        Assert.Equal(ReplyKind.Page, kind);
        Assert.DoesNotContain(reply.Ops, op => op.Kind == EditOpKind.MorphSubtree);
    }

    [Fact]
    public void Without_the_new_renders_markup_to_cut_from_the_refused_ops_stand_as_they_were()
    {
        var before = Reply.To(Div[Span["loading"]], Div[Span["loading"]]);
        var after = Reply.To(Div[Span["loading"]], Div[Ul[Li["a"]]]);
        var ops = new List<EditOp>();

        FrameDiffer.Diff(before.NewFrames, after.NewFrames, ops);

        Assert.Equal([EditOpKind.RemoveSubtree, EditOpKind.InsertSubtree], ops.Select(op => op.Kind));
        Assert.False(LiveDiffGate.DiffOpsAreClientSupported(ops));
    }

    [Fact]
    public void A_morph_no_smaller_than_the_page_is_still_answered_with_the_whole_page()
    {
        var reply = Reply.To(Div[Span["a"]], Div[Em["a"]]);

        var morph = Assert.Single(reply.Ops);

        Assert.Equal(EditOpKind.MorphSubtree, morph.Kind);
        Assert.Equal(ReplyKind.Page, reply.Kind);
        Assert.Equal(reply.PageBytes, reply.Bytes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_shape_arriving_before_another_inside_an_svg_is_one_morph_of_the_element_it_arrives_in(bool deeper)
    {
        Component Drawing(bool dot) => deeper
            ? Div[Svg[G[dot ? Circle.R("5") : null, G]]]
            : Div[Svg[dot ? Circle.R("5") : null, G]];
        var reply = Reply.To(Drawing(false), Drawing(true));

        reply.Apply();

        var morph = Assert.Single(reply.Ops);
        Assert.Equal(EditOpKind.MorphSubtree, morph.Kind);
        Assert.Equal(deeper ? [0, 0, 0] : [0, 0], morph.Path);
        Assert.Equal(reply.Expected, reply.Before.Html);
    }

    [Fact]
    public void A_shape_appended_inside_an_svg_is_still_the_trusted_insert_it_was()
    {
        var reply = Reply.To(Div[Svg[G]], Div[Svg[G, Circle.R("5")]]);

        var insert = Assert.Single(reply.Ops);

        Assert.Equal(EditOpKind.InsertSubtree, insert.Kind);
        Assert.True(insert.Trusted);
    }

    [Fact]
    public void An_opaque_child_of_the_morphed_parent_keeps_what_its_own_renderer_mounted()
    {
        var reply = Reply.To(
            Div[Div[Span["Loading"], new Island("{\"n\":1}")[Span["mounted by react"]]]],
            Div[Div[Ul[Li["a"]], new Island("{\"n\":2}")[Span["rendered by rask"], Span["again"]]]]);
        var mounted = reply.Before.At(0, 0, 1, 0);

        reply.Apply();

        Assert.Equal(EditOpKind.MorphSubtree, Assert.Single(reply.Ops).Kind);
        Assert.Same(mounted, Assert.Single(reply.Before.At(0, 0, 1).Children));
        Assert.Equal("{\"n\":2}", reply.Before.At(0, 0, 1).Attributes["props"]);
    }

    [Fact]
    public void A_handler_arriving_in_the_morphed_parent_brings_its_id_in_the_markup_and_renumbers_the_ones_after_it()
    {
        var show = false;
        var page = new StubComponent(() => Div[
            Button.OnClick(() => { })["Before"],
            Section[show ? Button.OnClick(() => { })["Cancel"] : Span["Nothing to cancel"], Button.OnClick(() => { })["Delete"]],
            P[Button.OnClick(() => { })["Archive"]]
        ]);
        var reply = Reply.ToLive(page, () => show = true);
        var archive = reply.Before.At(0, 2, 0);

        reply.Apply();

        Assert.Equal(
            [(EditOpKind.MorphSubtree, "0/1", null), (EditOpKind.SetAttribute, "0/2/0", "h3")],
            reply.Ops.Select(op => (op.Kind, string.Join('/', op.Path), op.Value)));
        Assert.Contains("data-rask-on-click=\"h1\"", Assert.Single(reply.Morphed), StringComparison.Ordinal);
        Assert.Contains("data-rask-on-click=\"h2\"", Assert.Single(reply.Morphed), StringComparison.Ordinal);
        Assert.Same(archive, reply.Before.At(0, 2, 0));
        Assert.Equal(reply.Expected, reply.Before.Html);
    }

    [Fact]
    public void The_render_after_a_morph_is_diffed_against_what_the_morph_brought()
    {
        var cache = new SessionRenderCache();
        var ops = new List<EditOp>();
        Render(cache, Div[Main[Span["Loading"], P["1 open"]]], ops);
        Render(cache, Div[Main[Ul[Li["a"]], P["1 open"]]], ops);
        var morphed = ops.ConvertAll(static op => op.Kind);

        Render(cache, Div[Main[Ul[Li["a"]], P["2 open"]]], ops);

        Assert.Equal([EditOpKind.MorphSubtree], morphed);
        var text = Assert.Single(ops);
        Assert.Equal(EditOpKind.UpdateText, text.Kind);
        Assert.Equal([0, 0, 1, 0], text.Path);
    }

    [Fact]
    public void What_is_said_of_a_morphed_level_names_the_parent_the_child_that_changed_shape_and_what_a_key_is_for()
    {
        var reply = Reply.To(Div[Main[Span["Loading"], P["after"]]], Div[Main[Ul[Li["a"]], P["after"]]]);
        var refused = new List<EditOp>
        {
            new(EditOpKind.RemoveSubtree, [0, 0, 0], null, null, 1),
            new(EditOpKind.InsertSubtree, [0, 0, 0], null, null, 1, htmlStart: reply.Html.IndexOf("<ul>", StringComparison.Ordinal),
                htmlEnd: reply.Html.IndexOf("<p>", StringComparison.Ordinal)),
        };

        var said = FullPageReply.Morphed([0, 0], 27, FullPageReply.ByPosition(refused, reply.Html));

        Assert.StartsWith(
            "Rask live: the children of /0/0 went out as one subtree (27 chars) instead of node by node: RemoveSubtree at /0/0/0 is by position, replaced by <ul><li>a</li></ul>.",
            said, StringComparison.Ordinal);
        Assert.Contains("needs a Key", said, StringComparison.Ordinal);
        Assert.DoesNotContain("whole page", said, StringComparison.Ordinal);
    }

    private static void Render(SessionRenderCache cache, Component tree, List<EditOp> ops)
    {
        var html = new StringBuilder();
        using (FrameSinkScope.Push(cache.PrepareCurrentBuffer()))
        {
            HtmlSerializer.Serialize(tree, html);
        }

        cache.TryComputeDiff(ops, html.ToString());
    }

    private sealed class Island(string props) : Component
    {
        protected override string? TagName => "rask-external";

        protected override bool OpaqueSubtree => true;

        protected override void WriteAttributes(StringBuilder sb) => AppendAttr(sb, "props", props);
    }
}
