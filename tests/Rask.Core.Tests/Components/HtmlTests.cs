namespace Rask.Core.Tests.Components;

public partial class HtmlTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() => Assert.Equal("<html></html>", Html.ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        Assert.Equal(
            // lang/dir are the GLOBAL attributes inherited from Element now (#693) rather than <html>'s
            // own, so they emit with the plain globals — before data-* — leaving xmlns as the only
            // html-specific attribute after it.
            "<html id=\"i\" class=\"c\" style=\"s\" lang=\"en\" dir=\"ltr\" data-k=\"v\" xmlns=\"http://www.w3.org/1999/xhtml\"></html>",
            Html
                .Lang("en")
                .Dir("ltr")
                .Xmlns("http://www.w3.org/1999/xhtml")
                .Id("i")
                .Class("c")
                .Style("s")
                .Data(new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<html>&lt;x&gt;</html>", Html["<x>"].ToHtml());
}
