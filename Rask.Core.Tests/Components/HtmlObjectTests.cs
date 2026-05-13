using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class HtmlObjectTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<object></object>", HtmlObject().ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
                Assert.Equal(
            "<object id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" data=\"/file.pdf\" type=\"application/pdf\" name=\"n\" width=\"800\" height=\"600\" form=\"f\" usemap=\"#m\"></object>",
            HtmlObject(DataUrl: "/file.pdf", Type: "application/pdf", Name: "n", Width: 800, Height: 600, Form: "f", UseMap: "#m", Id: "i", Class: "c", Style: "s", Data: new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<object>&lt;x&gt;</object>", HtmlObject()["<x>"].ToHtml());
}
