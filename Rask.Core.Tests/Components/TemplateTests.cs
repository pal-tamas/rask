using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class TemplateTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<template></template>", Template().ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        
        Assert.Equal(
            "<template id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></template>",
            Template(Id: "i", Class: "c", Style: "s", Data: new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<template>&lt;x&gt;</template>", Template()["<x>"].ToHtml());
}
