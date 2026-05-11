using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class BrTests
{
    [Fact]
    public void Render_NullProps_ReturnsSelfClosingTag() => Assert.Equal("<br />", new Br().ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        var props = new Br.Props(
            "i",
            "c",
            "s",
            new Dictionary<string, string?> { ["k"] = "v" });

        Assert.Equal(
            "<br id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" />",
            new Br(props).ToHtml());
    }
}
