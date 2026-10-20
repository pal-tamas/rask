using System.Text;
using Rask.Core.Live;

namespace Rask.Core.Tests.Live;

// A <tr> written straight under a <table> is put in a <tbody> by the browser's parser. The markup stays as it was
// written; the frames count the body the page will hold, so a path through the table names a node that is there.
public partial class ImpliedTableBodyTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Rows_straight_under_a_table_are_framed_in_a_body_the_markup_does_not_write()
    {
        var (frames, html) = Frames(Table.Id("t")[Tr[Td["a"]], Tr[Td["b"]]]);

        var dom = FrameDom.Of(frames);

        Assert.Equal("<table id=\"t\"><tr><td>a</td></tr><tr><td>b</td></tr></table>", html);
        Assert.Equal("<table id=\"t\"><tbody><tr><td>[a]</td></tr><tr><td>[b]</td></tr></tbody></table>", dom.Html);
        var body = frames.Single(static f => f.Name == "tbody");
        Assert.Equal("<tr><td>a</td></tr><tr><td>b</td></tr>", html[body.HtmlStart..body.HtmlEnd]);
    }

    [Fact]
    public void Each_run_of_rows_between_written_sections_gets_a_body_of_its_own()
    {
        var (frames, _) = Frames(Table[Caption["c"], Tr[Td["a"]], Thead[Tr[Th["h"]]], Tr[Td["b"]], Tr[Td["c"]], Tbody[Tr[Td["d"]]]]);

        var dom = FrameDom.Of(frames);

        Assert.Equal(
            "<table><caption>[c]</caption><tbody><tr><td>[a]</td></tr></tbody><thead><tr><th>[h]</th></tr></thead>"
            + "<tbody><tr><td>[b]</td></tr><tr><td>[c]</td></tr></tbody><tbody><tr><td>[d]</td></tr></tbody></table>",
            dom.Html);
    }

    [Fact]
    public void A_table_whose_rows_sit_in_written_sections_is_framed_as_written()
    {
        var (frames, _) = Frames(Div[Table[Thead[Tr[Th["h"]]], Tbody[Tr[Td["a"]]]]]);

        Assert.Equal(2, frames.Count(static f => f.Kind == RenderFrameKind.Element && f.Name is "thead" or "tbody"));
    }

    [Fact]
    public void A_text_after_the_rows_keeps_its_place_and_the_body_spans_only_the_rows()
    {
        var change = Reply.To(Div[Table[Tr[Td["a"]]], P["x"]], Div[Table[Tr[Td["a"]], Tr[Td["b"]]], P["y"]]);

        change.Apply();

        Assert.Equal(change.Expected, change.Before.Html);
        Assert.True(LiveDiffGate.DiffOpsAreClientSupported(change.Ops));
    }

    [Fact]
    public void The_first_row_of_a_bare_table_arrives_as_the_body_the_browser_would_put_it_in()
    {
        var change = Reply.To(Div[Table.Id("t")[Caption["c"]]], Div[Table.Id("t")[Caption["c"], Tr.Key("a")[Td["a"]]]]);

        change.Apply();

        var insert = Assert.Single(change.Ops);
        Assert.Equal(EditOpKind.InsertSubtree, insert.Kind);
        Assert.Equal("<tr data-rask-key=\"a\"><td>a</td></tr>", change.Html[insert.HtmlStart..insert.HtmlEnd]);
        Assert.Equal(change.Expected, change.Before.Html);
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
}
