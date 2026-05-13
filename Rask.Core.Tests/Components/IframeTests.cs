using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class IframeTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<iframe></iframe>", new Iframe().ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
                Assert.Equal(
            "<iframe id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" src=\"/page\" srcdoc=\"&lt;p&gt;x&lt;/p&gt;\" name=\"n\" sandbox=\"allow-scripts\" allow=\"camera\" width=\"640\" height=\"480\" loading=\"lazy\" referrerpolicy=\"no-referrer\"></iframe>",
            new Iframe { Src = "/page", Srcdoc = "<p>x</p>", Name = "n", Sandbox = "allow-scripts", Allow = "camera", Width = 640, Height = 480, Loading = "lazy", ReferrerPolicy = "no-referrer", Id = "i", Class = "c", Style = "s", Data = new Dictionary<string, string?> { ["k"] = "v" } }.ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<iframe>&lt;x&gt;</iframe>", new Iframe { Children = ["<x>"] }.ToHtml());
}
