using System.Text;
using Rask.Core.Live;

namespace Rask.Core.Tests.Live;

// A reply that falls back to the whole page says why, in Development. What it says is decided here, from the same
// ops and by the same rule (LiveDiffGate.FirstRefused) that refused the diff, so the reason can never name a node
// the gate would have let through.
public partial class FullPageReplyTests : global::Rask.Core.RaskMarkup
{
    private static (List<EditOp> Ops, string Html) Change(Component before, Component after)
    {
        var cache = new SessionRenderCache();
        var ops = new List<EditOp>();
        Render(cache, before, ops);

        return (ops, Render(cache, after, ops));
    }

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

    [Fact]
    public void A_child_that_changes_element_is_named_by_its_path_and_the_markup_that_replaces_it()
    {
        var (ops, html) = Change(
            Div[Span.Class("placeholder")["Choose…"], P["after"]],
            Div[Div.Class("picked")["Glock"], P["after"]]);

        var reason = FullPageReply.Reason(ops, rawAtRoot: false, diffBytes: 0, html);

        Assert.StartsWith("RemoveSubtree at /0/0 is by position, replaced by <div class=\"picked\">Glock</div>. ", reason, StringComparison.Ordinal);
        Assert.Contains("carry a Key", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void A_child_that_arrives_before_its_siblings_is_named_by_the_markup_inserted()
    {
        var (ops, html) = Change(
            Div[P["before"], P["after"]],
            Div[P["before"], Aside.Class("toast")["Saved"], P["after"]]);

        var reason = FullPageReply.Reason(ops, rawAtRoot: false, diffBytes: 0, html);

        Assert.Contains("is by position", reason, StringComparison.Ordinal);
        Assert.Contains("<aside class=\"toast\">Saved</aside>", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Long_markup_is_cut_so_the_line_stays_a_line()
    {
        var (ops, html) = Change(
            Div[Span["a"], P["after"]],
            Div[Div[new string('x', 400)], P["after"]]);

        var reason = FullPageReply.Reason(ops, rawAtRoot: false, diffBytes: 0, html);

        Assert.Contains(new string('x', 100) + "…", reason, StringComparison.Ordinal);
        Assert.DoesNotContain(new string('x', 200), reason, StringComparison.Ordinal);
    }

    [Fact]
    public void A_diff_that_lost_to_the_page_on_size_says_how_large_it_was()
    {
        var (ops, html) = Change(Div[P["one"]], Div[P["two"]]);

        var reason = FullPageReply.Reason(ops, rawAtRoot: false, diffBytes: 9_000, html);

        Assert.Equal("The diff was 9000 bytes, no smaller than the page itself.", reason);
    }

    [Fact]
    public void Raw_markup_at_the_documents_own_level_is_named_as_the_reason()
    {
        var reason = FullPageReply.Reason([], rawAtRoot: true, diffBytes: 0, "<p>x</p>");

        Assert.StartsWith("Raw markup sits beside other nodes at the document's own level", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void A_diff_the_gate_would_ship_has_no_reason_to_give()
    {
        var (ops, html) = Change(Div[P["one"], P["after"]], Div[P["two"], P["after"]]);

        var reason = FullPageReply.Reason(ops, rawAtRoot: false, diffBytes: 0, html);

        Assert.NotEmpty(ops);
        Assert.Null(reason);
    }

    [Theory]
    [InlineData(true, "sign-in handoff")]
    [InlineData(false, "file download")]
    public void A_render_that_never_reaches_the_differ_names_what_it_carries(bool signIn, string carried)
    {
        var reason = FullPageReply.OutOfBand(signIn);

        var whole = reason.Contains("travels with a whole page", StringComparison.Ordinal);

        Assert.Contains(carried, reason, StringComparison.Ordinal);
        Assert.True(whole);
    }

    [Fact]
    public void A_render_with_nothing_to_compare_with_and_one_with_nothing_to_patch_each_have_words_of_their_own()
    {
        var first = FullPageReply.NoEarlierRender;

        var unchanged = FullPageReply.NothingToPatch;

        Assert.Contains("no earlier render", first, StringComparison.Ordinal);
        Assert.Contains("no change in the tree", unchanged, StringComparison.Ordinal);
        Assert.NotEqual(first, unchanged);
    }
}
