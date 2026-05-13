using Rask.Core.Components;

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
            Area(Alt: "alt", Coords: "0,0,10,10", Shape: "rect", Href: "/x", Target: "_blank", Rel: "noopener", Download: "f.zip", Id: "i", Class: "c", Style: "s", Data: new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }
}
