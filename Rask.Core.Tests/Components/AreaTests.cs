namespace Rask.Core.Tests.Components;

public class AreaTests
{
    [Fact]
    public void Render_NullProps_ReturnsSelfClosingTag() =>
        Assert.Equal("<area />", Area().ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        Assert.Equal(
            "<area id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" alt=\"alt\" coords=\"0,0,10,10\" shape=\"rect\" href=\"/x\" target=\"_blank\" rel=\"noopener\" download=\"f.zip\" />",
            Area("alt", "0,0,10,10", "rect", "/x", "_blank", "noopener", "f.zip", "i", "c", "s",
                new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }
}
