using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class LinkTests
{
    [Fact]
    public void Render_NullProps_ReturnsSelfClosingTag() => Assert.Equal("<link />", new Link().ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        var props = new Link.Props(
            "/style.css",
            "stylesheet",
            "text/css",
            "all",
            "16x16",
            "en",
            "style",
            "anonymous",
            "no-referrer",
            true,
            "#fff",
            "i",
            "c",
            "s",
            new Dictionary<string, string?> { ["k"] = "v" });

        Assert.Equal(
            "<link id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" href=\"/style.css\" rel=\"stylesheet\" type=\"text/css\" media=\"all\" sizes=\"16x16\" hreflang=\"en\" as=\"style\" crossorigin=\"anonymous\" referrerpolicy=\"no-referrer\" disabled color=\"#fff\" />",
            new Link(props).ToHtml());
    }
}
