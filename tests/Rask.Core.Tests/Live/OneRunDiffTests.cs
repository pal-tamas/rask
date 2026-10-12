using System.Text;
using Rask.Core.Live;

#pragma warning disable RASK014 // the opaque host is a test-defined component with no generated entry
#pragma warning disable RASK022 // a keyed child beside unkeyed ones is the subject of some of these tests

namespace Rask.Core.Tests.Live;

// A child that comes or goes among siblings that otherwise still pair up — `saved ? Callout : null` above a
// form — is ONE run of children, and ships as trusted inserts or removes with the siblings around it diffed
// as the pairs they are. Before, the positional walk paired by slot, saw every later sibling as replaced,
// and the gate answered with the whole page.
//
// Every diff here is also REPLAYED (FrameDom): the ops, applied in the order they were written, must leave
// the document the new render describes, and a sibling that stayed must be the node it was.
public partial class OneRunDiffTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void A_child_arriving_above_a_fieldset_is_one_trusted_insert_and_the_fieldset_is_not_touched()
    {
        var change = Change(
            Div[Fieldset[Input.Value("").Name("email")]],
            Div[Aside["Saved"], Fieldset[Input.Value("").Name("email")]]);
        var fieldset = change.Before.At(0, 0);

        change.Apply();

        var insert = Assert.Single(change.Ops);
        Assert.Equal(EditOpKind.InsertSubtree, insert.Kind);
        Assert.Equal([0, 0], insert.Path);
        Assert.True(LiveDiffGate.DiffOpsAreClientSupported(change.Ops));
        Assert.Same(fieldset, change.Before.At(0, 1));
        Assert.Equal(change.Expected, change.Before.Html);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void A_child_arriving_among_siblings_of_other_tags_is_one_trusted_insert_at_its_place(int at)
    {
        var change = Change(Div[Siblings(null)], Div[Siblings(at)]);

        change.Apply();

        var insert = Assert.Single(change.Ops);
        Assert.Equal(EditOpKind.InsertSubtree, insert.Kind);
        Assert.True(insert.Trusted);
        Assert.Equal([0, at], insert.Path);
        Assert.Equal(change.Expected, change.Before.Html);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void A_child_leaving_from_among_siblings_of_other_tags_is_one_trusted_remove_at_its_place(int at)
    {
        var change = Change(Div[Siblings(at)], Div[Siblings(null)]);

        change.Apply();

        var remove = Assert.Single(change.Ops);
        Assert.Equal(EditOpKind.RemoveSubtree, remove.Kind);
        Assert.True(remove.Trusted);
        Assert.Equal([0, at], remove.Path);
        Assert.Equal(change.Expected, change.Before.Html);
    }

    [Fact]
    public void Two_children_arriving_side_by_side_are_one_run()
    {
        var change = Change(
            Div[H1["Orders"], Ul[Li["a"]]],
            Div[H1["Orders"], Aside["Saved"], P["Nothing else to do."], Ul[Li["a"]]]);

        change.Apply();

        Assert.Equal([EditOpKind.InsertSubtree, EditOpKind.InsertSubtree], change.Ops.Select(op => op.Kind));
        Assert.Equal([[0, 1], [0, 2]], change.Ops.Select(op => op.Path));
        Assert.True(LiveDiffGate.DiffOpsAreClientSupported(change.Ops));
        Assert.Equal(change.Expected, change.Before.Html);
    }

    [Fact]
    public void Two_children_arriving_apart_from_each_other_are_still_answered_with_the_whole_page()
    {
        var change = Change(
            Div[H1["Orders"], Ul[Li["a"]]],
            Div[Aside["Saved"], H1["Orders"], P["Nothing else to do."], Ul[Li["a"]]]);

        var refused = !LiveDiffGate.DiffOpsAreClientSupported(change.Ops);

        Assert.True(refused);
        Assert.DoesNotContain(change.Ops, op => op.Trusted);
    }

    [Fact]
    public void A_child_arriving_and_a_sibling_changing_element_at_once_is_still_answered_with_the_whole_page()
    {
        var change = Change(
            Div[H1["Orders"], Span["a"], Ul[Li["a"]]],
            Div[H1["Orders"], Aside["Saved"], Em["a"], Ul[Li["a"]]]);

        var refused = !LiveDiffGate.DiffOpsAreClientSupported(change.Ops);

        Assert.True(refused);
    }

    [Fact]
    public void An_element_in_place_of_another_is_still_answered_with_the_whole_page()
    {
        var change = Change(Div[H1["Orders"], Span["loading"]], Div[H1["Orders"], Ul[Li["a"]]]);

        var refused = !LiveDiffGate.DiffOpsAreClientSupported(change.Ops);

        Assert.True(refused);
    }

    [Fact]
    public void An_element_in_place_of_text_is_still_answered_with_the_whole_page()
    {
        var change = Change(Div[H1["Orders"], "loading"], Div[H1["Orders"], Ul[Li["a"]]]);

        var refused = !LiveDiffGate.DiffOpsAreClientSupported(change.Ops);

        Assert.True(refused);
    }

    [Fact]
    public void A_child_arriving_at_the_documents_own_level_is_still_answered_with_the_whole_page()
    {
        var change = Change([Fieldset[Input.Value("")]], [Aside["Saved"], Fieldset[Input.Value("")]]);

        var refused = !LiveDiffGate.DiffOpsAreClientSupported(change.Ops);

        Assert.True(refused);
    }

    [Fact]
    public void A_child_arriving_while_a_sibling_below_changes_an_attribute_ships_both_at_the_new_slots()
    {
        var change = Change(
            Div[Fieldset.Class("idle")[Input.Value("a").Name("email")]],
            Div[Aside["Saved"], Fieldset.Class("sent")[Input.Value("ab").Name("email")]]);
        var input = change.Before.At(0, 0, 0);

        change.Apply();

        Assert.Equal(
            [(EditOpKind.InsertSubtree, "0/0"), (EditOpKind.SetAttribute, "0/1"), (EditOpKind.SetAttribute, "0/1/0")],
            change.Ops.Select(op => (op.Kind, string.Join('/', op.Path))));
        Assert.Same(input, change.Before.At(0, 1, 0));
        Assert.Equal(change.Expected, change.Before.Html);
    }

    [Fact]
    public void A_sibling_before_the_run_is_patched_before_the_run_goes_in()
    {
        var change = Change(
            Div[H1["1 order"], Fieldset[Input.Value("")]],
            Div[H1["2 orders"], Aside["Saved"], Fieldset[Input.Value("")]]);

        change.Apply();

        Assert.Equal([EditOpKind.UpdateText, EditOpKind.InsertSubtree], change.Ops.Select(op => op.Kind));
        Assert.Equal(change.Expected, change.Before.Html);
    }

    [Fact]
    public void Children_arriving_at_two_depths_at_once_are_a_run_at_each()
    {
        var change = Change(
            Div[Section[Fieldset[Input.Value("").Name("email")]]],
            Div[Section[Aside["Saved"], Fieldset[P["Enter an email address."], Input.Value("").Name("email")]]]);
        var input = change.Before.At(0, 0, 0, 0);

        change.Apply();

        Assert.Equal([[0, 0, 0], [0, 0, 1, 0]], change.Ops.Select(op => op.Path));
        Assert.All(change.Ops, op => Assert.True(op.Trusted));
        Assert.Same(input, change.Before.At(0, 0, 1, 1));
        Assert.Equal(change.Expected, change.Before.Html);
    }

    [Fact]
    public void A_child_arriving_inside_a_row_of_a_keyed_list_is_a_run_in_that_row()
    {
        var change = Change(
            Ul[Li.Key("a")[Span["Alpha"]], Li.Key("b")[Span["Beta"]]],
            Ul[Li.Key("a")[Span["Alpha"]], Li.Key("b")[Em["new"], Span["Beta"]]]);
        var beta = change.Before.At(0, 1, 0);

        change.Apply();

        var insert = Assert.Single(change.Ops);
        Assert.Equal([0, 1, 0], insert.Path);
        Assert.True(insert.Trusted);
        Assert.Same(beta, change.Before.At(0, 1, 1));
        Assert.Equal(change.Expected, change.Before.Html);
    }

    [Fact]
    public void A_child_arriving_before_a_keyed_sibling_leaves_the_keyed_sibling_the_node_it_was()
    {
        var change = Change(
            Div[Li.Key("a")["Alpha"], Span["after"]],
            Div[Li["new"], Li.Key("a")["Alpha"], Span["after"]]);
        var alpha = change.Before.At(0, 0);

        change.Apply();

        var insert = Assert.Single(change.Ops);
        Assert.Equal([0, 0], insert.Path);
        Assert.Same(alpha, change.Before.At(0, 1));
        Assert.Equal(change.Expected, change.Before.Html);
    }

    [Fact]
    public void Two_siblings_with_different_keys_are_never_paired_around_a_run()
    {
        var change = Change(
            Div[Span["before"], Li.Key("a")["Alpha"]],
            Div[Span["before"], Li.Key("b")["Beta"], Li.Key("a")["Alpha"]]);
        var alpha = change.Before.At(0, 1);

        change.Apply();

        var insert = Assert.Single(change.Ops);
        Assert.Equal([0, 1], insert.Path);
        Assert.Same(alpha, change.Before.At(0, 2));
        Assert.Equal(change.Expected, change.Before.Html);
    }

    [Fact]
    public void A_row_arriving_at_the_top_of_an_unkeyed_list_goes_in_at_the_top()
    {
        var change = Change(Ul[Li["a"], Li["b"]], Ul[Li["new"], Li["a"], Li["b"]]);
        var first = change.Before.At(0, 0);

        change.Apply();

        var insert = Assert.Single(change.Ops);
        Assert.Equal([0, 0], insert.Path);
        Assert.Same(first, change.Before.At(0, 1));
        Assert.Equal(change.Expected, change.Before.Html);
    }

    [Fact]
    public void A_row_leaving_the_middle_of_an_unkeyed_list_is_the_row_that_is_removed()
    {
        var change = Change(Ul[Li["a"], Li["b"], Li["c"]], Ul[Li["a"], Li["c"]]);
        var last = change.Before.At(0, 2);

        change.Apply();

        var remove = Assert.Single(change.Ops);
        Assert.Equal(EditOpKind.RemoveSubtree, remove.Kind);
        Assert.Equal([0, 1], remove.Path);
        Assert.Same(last, change.Before.At(0, 1));
        Assert.Equal(change.Expected, change.Before.Html);
    }

    [Fact]
    public void A_callout_arriving_above_same_tag_fields_leaves_the_field_being_typed_in_the_node_it_was()
    {
        var change = Change(
            Div[Div.Class("field")[Input.Value("Ad").Name("name")], Div.Class("field")[Input.Value("").Name("email")]],
            Div[
                Div.Class("callout")["That name is taken."],
                Div.Class("field")[Input.Value("Ada").Name("name")],
                Div.Class("field")[Input.Value("").Name("email")]
            ]);
        var typedIn = change.Before.At(0, 0, 0);

        change.Apply();

        Assert.Equal([(EditOpKind.InsertSubtree, "0/0"), (EditOpKind.SetAttribute, "0/1/0")],
            change.Ops.Select(op => (op.Kind, string.Join('/', op.Path))));
        Assert.Same(typedIn, change.Before.At(0, 1, 0));
        Assert.Equal(change.Expected, change.Before.Html);
    }

    [Theory]
    [InlineData("a,b", "a,b,c", 2)]
    [InlineData("a,a", "a,a,a", 2)]
    [InlineData("a,b", "a,c,d", 2)]
    [InlineData("a,b", "c,d,e", 2)]
    public void A_row_appended_to_an_unkeyed_list_stays_the_append_it_was_unless_another_place_is_strictly_better(
        string before, string after, int at)
    {
        var change = Change(Ul[Rows(before)], Ul[Rows(after)]);

        change.Apply();

        var insert = Assert.Single(change.Ops, op => op.Kind == EditOpKind.InsertSubtree);
        Assert.Equal([0, at], insert.Path);
        Assert.True(LiveDiffGate.DiffOpsAreClientSupported(change.Ops));
        Assert.Equal(change.Expected, change.Before.Html);
    }

    [Theory]
    [InlineData("a,b,c", "a,b", 2)]
    [InlineData("a,a,a", "a,a", 2)]
    [InlineData("a,b,c", "a,d", 2)]
    public void A_row_cut_from_the_end_of_an_unkeyed_list_stays_the_truncation_it_was(string before, string after, int at)
    {
        var change = Change(Ul[Rows(before)], Ul[Rows(after)]);

        change.Apply();

        var remove = Assert.Single(change.Ops, op => op.Kind == EditOpKind.RemoveSubtree);
        Assert.Equal([0, at], remove.Path);
        Assert.True(LiveDiffGate.DiffOpsAreClientSupported(change.Ops));
        Assert.Equal(change.Expected, change.Before.Html);
    }

    [Fact]
    public void A_sibling_that_only_kept_its_own_attributes_still_outweighs_one_that_kept_nothing()
    {
        var change = Change(
            Div[Div.Class("field")["one"], Div.Class("field")["two"]],
            Div[Div.Class("callout")["Saved"], Div.Class("field")["1"], Div.Class("field")["2"]]);

        change.Apply();

        Assert.Equal(EditOpKind.InsertSubtree, change.Ops[0].Kind);
        Assert.Equal([0, 0], change.Ops[0].Path);
        Assert.Equal(change.Expected, change.Before.Html);
    }

    [Fact]
    public void Past_256_places_to_put_it_a_row_added_to_an_unkeyed_list_is_appended_as_before()
    {
        var rows = string.Join(',', Enumerable.Range(0, 300));
        var change = Change(Ul[Rows(rows)], Ul[Rows("new," + rows)]);

        change.Apply();

        var insert = Assert.Single(change.Ops, op => op.Kind == EditOpKind.InsertSubtree);
        Assert.Equal([0, 300], insert.Path);
        Assert.True(LiveDiffGate.DiffOpsAreClientSupported(change.Ops));
        Assert.Equal(change.Expected, change.Before.Html);
    }

    [Fact]
    public void An_element_leaving_from_between_two_texts_takes_one_of_them_with_it()
    {
        // The browser holds ONE text node where two texts meet, and so does the frame stream.
        var change = Change(Div["Signed in as ", B["Ada"], "."], Div["Signed in as ", "."]);

        change.Apply();

        Assert.Equal([EditOpKind.UpdateText, EditOpKind.RemoveSubtree, EditOpKind.RemoveSubtree], change.Ops.Select(op => op.Kind));
        Assert.True(LiveDiffGate.DiffOpsAreClientSupported(change.Ops));
        Assert.Equal("<div>[Signed in as .]</div>", change.Before.Html);
        Assert.Equal(change.Expected, change.Before.Html);
    }

    [Fact]
    public void An_element_arriving_inside_a_text_splits_it_in_two()
    {
        var change = Change(Div["Signed in as ", "."], Div["Signed in as ", B["Ada"], "."]);

        change.Apply();

        Assert.True(LiveDiffGate.DiffOpsAreClientSupported(change.Ops));
        Assert.Equal("<div>[Signed in as ]<b>[Ada]</b>[.]</div>", change.Before.Html);
        Assert.Equal(change.Expected, change.Before.Html);
    }

    [Fact]
    public void An_opaque_sibling_after_the_run_has_its_attributes_patched_and_its_children_left_alone()
    {
        var change = Change(
            Div[new Island("{\"n\":1}")[Span["mounted by react"]]],
            Div[Aside["Saved"], new Island("{\"n\":2}")[Span["rendered by rask"], Span["again"]]]);
        var mounted = change.Before.At(0, 0, 0);

        change.Apply();

        Assert.Equal([(EditOpKind.InsertSubtree, "0/0"), (EditOpKind.SetAttribute, "0/1")],
            change.Ops.Select(op => (op.Kind, string.Join('/', op.Path))));
        Assert.Same(mounted, Assert.Single(change.Before.At(0, 1).Children));
    }

    [Fact]
    public void A_child_arriving_beside_raw_markup_is_still_one_morph_of_that_parent()
    {
        var change = Change(
            Div[Raw.Value("<b>one</b><i>two</i>"), Fieldset[Input.Value("")]],
            Div[Aside["Saved"], Raw.Value("<b>one</b><i>two</i>"), Fieldset[Input.Value("")]]);

        var op = Assert.Single(change.Ops);

        Assert.Equal(EditOpKind.MorphSubtree, op.Kind);
        Assert.Equal([0], op.Path);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_child_arriving_inside_an_svg_is_still_answered_with_the_whole_page(bool deeper)
    {
        Component Drawing(bool dot) => deeper
            ? Div[Svg[G[dot ? Circle.R("5") : null, G]]]
            : Div[Svg[dot ? Circle.R("5") : null, G]];
        var change = Change(Drawing(false), Drawing(true));

        var refused = !LiveDiffGate.DiffOpsAreClientSupported(change.Ops);

        Assert.True(refused);
    }

    [Fact]
    public void An_svg_arriving_among_html_siblings_is_a_trusted_insert()
    {
        var change = Change(Span["New"], Span[Svg[Circle.R("9")], "New"]);

        change.Apply();

        var insert = Assert.Single(change.Ops);
        Assert.Equal([0, 0], insert.Path);
        Assert.True(insert.Trusted);
        Assert.Equal(change.Expected, change.Before.Html);
    }

    [Fact]
    public void A_handler_arriving_above_carries_the_renumbered_ids_to_the_siblings_below()
    {
        var show = false;
        var page = new StubComponent(() => Div[
            show ? Button.OnClick(() => { })["Cancel"] : null,
            Button.OnClick(() => { })["Delete"],
            P[Button.OnClick(() => { })["Archive"]]
        ]);
        var (oldFrames, _) = Live(page);
        show = true;
        var (newFrames, html) = Live(page);
        var change = new Diffed(oldFrames, newFrames, html);
        var delete = change.Before.At(0, 0);

        change.Apply();

        Assert.Equal(
            [(EditOpKind.InsertSubtree, "0/0", null), (EditOpKind.SetAttribute, "0/1", "h1"), (EditOpKind.SetAttribute, "0/2/0", "h2")],
            change.Ops.Select(op => (op.Kind, string.Join('/', op.Path), op.Value)));
        Assert.Contains("data-rask-on-click=\"h0\"", html[change.Ops[0].HtmlStart..change.Ops[0].HtmlEnd], StringComparison.Ordinal);
        Assert.Same(delete, change.Before.At(0, 1));
        Assert.Equal(change.Expected, change.Before.Html);
    }

    [Fact]
    public void A_handler_leaving_from_above_carries_the_renumbered_ids_to_the_siblings_below()
    {
        var show = true;
        var page = new StubComponent(() => Div[
            show ? Button.OnClick(() => { })["Cancel"] : null,
            Button.OnClick(() => { })["Delete"]
        ]);
        var (oldFrames, _) = Live(page);
        show = false;
        var (newFrames, html) = Live(page);
        var change = new Diffed(oldFrames, newFrames, html);
        var delete = change.Before.At(0, 1);

        change.Apply();

        Assert.Equal(
            [(EditOpKind.RemoveSubtree, "0/0", null), (EditOpKind.SetAttribute, "0/0", "h0")],
            change.Ops.Select(op => (op.Kind, string.Join('/', op.Path), op.Value)));
        Assert.Same(delete, change.Before.At(0, 0));
        Assert.Equal(change.Expected, change.Before.Html);
    }

    private static IEnumerable<Component> Siblings(int? extraAt)
    {
        Component[] kept = [H1["Orders"], P["Two open."], Ul[Li["a"]]];
        for (var i = 0; i <= kept.Length; i++)
        {
            if (i == extraAt)
            {
                yield return Aside["Saved"];
            }

            if (i < kept.Length)
            {
                yield return kept[i];
            }
        }
    }

    private static IEnumerable<Component> Rows(string labels) => labels.Split(',').Select(label => (Component)Li[label]);

    private static Diffed Change(Component before, Component after)
    {
        var (afterFrames, html) = Frames(after);
        return new Diffed(Frames(before).Frames, afterFrames, html);
    }

    private static (RenderFrame[] Frames, string Html) Frames(Component tree)
    {
        var html = new StringBuilder();
        var frames = new FrameWriter();
        using (FrameSinkScope.Push(frames))
        {
            HtmlSerializer.Serialize(tree, html);
        }

        return (frames.WrittenSpan.ToArray(), html.ToString());
    }

    private static (RenderFrame[] Frames, string Html) Live(Component root)
    {
        var frames = new FrameWriter();
        string html;
        using (FrameSinkScope.Push(frames))
        {
            html = root.RenderAsLiveRoot();
        }

        return (frames.WrittenSpan.ToArray(), html);
    }

    // Two renders and the diff between them, with the first as a document the ops can be applied to.
    private sealed class Diffed
    {
        private readonly RenderFrame[] _newFrames;

        internal Diffed(RenderFrame[] oldFrames, RenderFrame[] newFrames, string html)
        {
            _newFrames = newFrames;
            Before = FrameDom.Of(oldFrames);
            Expected = FrameDom.Of(newFrames).Html;
            FrameDiffer.Diff(oldFrames, newFrames, Ops, html);
        }

        internal FrameDom Before { get; }

        internal string Expected { get; }

        internal List<EditOp> Ops { get; } = [];

        internal void Apply() => Before.Apply(Ops, _newFrames);
    }

    private sealed class Island(string props) : Component
    {
        protected override string? TagName => "rask-external";

        protected override bool OpaqueSubtree => true;

        protected override void WriteAttributes(StringBuilder sb) => AppendAttr(sb, "props", props);
    }
}
