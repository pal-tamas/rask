using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class TitleTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() => Assert.Equal("<title></title>", new Title(null).ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        var props = new Title.Props(
            "i",
            "c",
            "s",
            new Dictionary<string, string?> { ["k"] = "v" });

        Assert.Equal(
            "<title id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></title>",
            new Title(props).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<title>&lt;x&gt;</title>", new Title(null, "<x>").ToHtml());
}
