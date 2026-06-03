namespace Rask.Core.Tests.Components;

public class OptgroupTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<optgroup></optgroup>", Optgroup().ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        Assert.Equal(
            "<optgroup id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" disabled label=\"Group\"></optgroup>",
            Optgroup(true, "Group", "i", "c", "s", new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<optgroup>&lt;x&gt;</optgroup>", Optgroup()["<x>"].ToHtml());
}
