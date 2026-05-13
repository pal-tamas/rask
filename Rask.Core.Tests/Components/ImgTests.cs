using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class ImgTests
{
    [Fact]
    public void Render_NullProps_ReturnsSelfClosingTag() => Assert.Equal("<img />", new Img().ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        
        Assert.Equal(
            "<img id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" src=\"/a.png\" alt=\"alt text\" width=\"100\" height=\"50\" loading=\"lazy\" srcset=\"/a.png 1x, /a@2x.png 2x\" sizes=\"100vw\" crossorigin=\"anonymous\" referrerpolicy=\"no-referrer\" decoding=\"async\" usemap=\"#m\" ismap />",
            new Img { Src = "/a.png", Alt = "alt text", Width = 100, Height = 50, Loading = "lazy", Srcset = "/a.png 1x, /a@2x.png 2x", Sizes = "100vw", CrossOrigin = "anonymous", ReferrerPolicy = "no-referrer", Decoding = "async", UseMap = "#m", Ismap = true, Id = "i", Class = "c", Style = "s", Data = new Dictionary<string, string?> { ["k"] = "v" } }.ToHtml());
    }
}
