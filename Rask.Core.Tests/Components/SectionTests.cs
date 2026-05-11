using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class SectionTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<section></section>", new Section(null).ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        var props = new Section.Props("i", "c", "s",
            new Dictionary<string, string?> { ["k"] = "v" });
        Assert.Equal("<section id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></section>",
            new Section(props).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<section>&lt;x&gt;</section>", new Section(null, "<x>").ToHtml());
}
