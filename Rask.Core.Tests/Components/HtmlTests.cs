using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class HtmlTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() => Assert.Equal("<html></html>", new Html(null).ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        var props = new Html.Props(
            "en",
            "ltr",
            "http://www.w3.org/1999/xhtml",
            "i",
            "c",
            "s",
            new Dictionary<string, string?> { ["k"] = "v" });

        Assert.Equal(
            "<html id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" lang=\"en\" dir=\"ltr\" xmlns=\"http://www.w3.org/1999/xhtml\"></html>",
            new Html(props).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<html>&lt;x&gt;</html>", new Html(null, "<x>").ToHtml());
}
