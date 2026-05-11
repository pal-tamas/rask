using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class BaseTests
{
    [Fact]
    public void Render_NullProps_ReturnsSelfClosingTag() => Assert.Equal("<base />", new Base().ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        var props = new Base.Props(
            "/",
            "_blank",
            "i",
            "c",
            "s",
            new Dictionary<string, string?> { ["k"] = "v" });

        Assert.Equal(
            "<base id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" href=\"/\" target=\"_blank\" />",
            new Base(props).ToHtml());
    }
}
