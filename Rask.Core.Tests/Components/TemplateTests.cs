using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class TemplateTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<template></template>", new Template(null).ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        var props = new Template.Props(
            "i",
            "c",
            "s",
            new Dictionary<string, string?> { ["k"] = "v" });

        Assert.Equal(
            "<template id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></template>",
            new Template(props).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<template>&lt;x&gt;</template>", new Template(null, "<x>").ToHtml());
}
