#pragma warning disable RASK023 // null-props test deliberately omits Alt to assert bare rendering

namespace Rask.Core.Tests.Components;

public class ImgTests
{
    [Fact]
    public void Render_NullProps_ReturnsSelfClosingTag() => Assert.Equal("<img />", Img().ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        Assert.Equal(
            "<img id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" src=\"/a.png\" alt=\"alt text\" width=\"100\" height=\"50\" loading=\"lazy\" srcset=\"/a.png 1x, /a@2x.png 2x\" sizes=\"100vw\" crossorigin=\"anonymous\" referrerpolicy=\"no-referrer\" decoding=\"async\" usemap=\"#m\" ismap />",
            Img("/a.png", "alt text", 100, 50, "lazy", "/a.png 1x, /a@2x.png 2x", "100vw", "anonymous", "no-referrer",
                "async", "#m", true, "i", "c", "s", new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }
}
