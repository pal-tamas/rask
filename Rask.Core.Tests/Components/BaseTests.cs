using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class BaseTests
{
    [Fact]
    public void Render_NullProps_ReturnsSelfClosingTag() => Assert.Equal("<base />", new Base().ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        
        Assert.Equal(
            "<base id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" href=\"/\" target=\"_blank\" />",
            new Base { Href = "/", Target = "_blank", Id = "i", Class = "c", Style = "s", Data = new Dictionary<string, string?> { ["k"] = "v" } }.ToHtml());
    }
}
