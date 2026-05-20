namespace Rask.Core.Tests.Components;

public class IframeTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<iframe></iframe>", Iframe().ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        Assert.Equal(
            "<iframe id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" src=\"/page\" srcdoc=\"&lt;p&gt;x&lt;/p&gt;\" name=\"n\" sandbox=\"allow-scripts\" allow=\"camera\" width=\"640\" height=\"480\" loading=\"lazy\" referrerpolicy=\"no-referrer\"></iframe>",
            Iframe("/page", "<p>x</p>", "n", "allow-scripts", "camera", 640, 480, "lazy", "no-referrer", "i", "c", "s",
                new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<iframe>&lt;x&gt;</iframe>", Iframe()["<x>"].ToHtml());
}
