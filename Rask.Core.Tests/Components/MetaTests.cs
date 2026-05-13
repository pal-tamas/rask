using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class MetaTests
{
    [Fact]
    public void Render_NullProps_ReturnsSelfClosingTag() => Assert.Equal("<meta />", new Meta().ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        
        Assert.Equal(
            "<meta id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" charset=\"utf-8\" name=\"viewport\" content=\"width=device-width\" http-equiv=\"X-UA-Compatible\" />",
            new Meta { Charset = "utf-8", Name = "viewport", Content = "width=device-width", HttpEquiv = "X-UA-Compatible", Id = "i", Class = "c", Style = "s", Data = new Dictionary<string, string?> { ["k"] = "v" } }.ToHtml());
    }
}
