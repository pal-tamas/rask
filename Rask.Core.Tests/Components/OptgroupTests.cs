using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class OptgroupTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<optgroup></optgroup>", new Optgroup(null).ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        var props = new Optgroup.Props(true, "Group",
            "i", "c", "s",
            new Dictionary<string, string?> { ["k"] = "v" });
        Assert.Equal(
            "<optgroup id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" disabled label=\"Group\"></optgroup>",
            new Optgroup(props).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<optgroup>&lt;x&gt;</optgroup>", new Optgroup(null, "<x>").ToHtml());
}
