namespace Rask.Core.Tests.Components;

public class BaseTests
{
    [Fact]
    public void Render_NullProps_ReturnsSelfClosingTag() => Assert.Equal("<base />", Base().ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        Assert.Equal(
            "<base id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" href=\"/\" target=\"_blank\" />",
            Base("/", "_blank", "i", "c", "s", new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }
}
