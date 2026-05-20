namespace Rask.Core.Tests.Components;

public class MetaTests
{
    [Fact]
    public void Render_NullProps_ReturnsSelfClosingTag() => Assert.Equal("<meta />", Meta().ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        Assert.Equal(
            "<meta id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" charset=\"utf-8\" name=\"viewport\" content=\"width=device-width\" http-equiv=\"X-UA-Compatible\" />",
            Meta("utf-8", "viewport", "width=device-width", "X-UA-Compatible", "i", "c", "s",
                new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }
}
