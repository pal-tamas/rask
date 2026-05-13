using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class HtmlTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() => Assert.Equal("<html></html>", new Html().ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        
        Assert.Equal(
            "<html id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" lang=\"en\" dir=\"ltr\" xmlns=\"http://www.w3.org/1999/xhtml\"></html>",
            new Html { Lang = "en", Dir = "ltr", Xmlns = "http://www.w3.org/1999/xhtml", Id = "i", Class = "c", Style = "s", Data = new Dictionary<string, string?> { ["k"] = "v" } }.ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<html>&lt;x&gt;</html>", new Html { Children = ["<x>"] }.ToHtml());
}
