using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class LinkTests
{
    [Fact]
    public void Render_NullProps_ReturnsSelfClosingTag() => Assert.Equal("<link />", Link().ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        
        Assert.Equal(
            "<link id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" href=\"/style.css\" rel=\"stylesheet\" type=\"text/css\" media=\"all\" sizes=\"16x16\" hreflang=\"en\" as=\"style\" crossorigin=\"anonymous\" referrerpolicy=\"no-referrer\" disabled color=\"#fff\" />",
            Link(Href: "/style.css", Rel: "stylesheet", Type: "text/css", Media: "all", Sizes: "16x16", Hreflang: "en", As: "style", CrossOrigin: "anonymous", ReferrerPolicy: "no-referrer", Disabled: true, Color: "#fff", Id: "i", Class: "c", Style: "s", Data: new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }
}
