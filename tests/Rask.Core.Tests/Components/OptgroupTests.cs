namespace Rask.Core.Tests.Components;

public partial class OptgroupTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<optgroup></optgroup>", Optgroup.ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        Assert.Equal(
            "<optgroup id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" disabled label=\"Group\"></optgroup>",
            Optgroup
                .Disabled(true)
                .Label("Group")
                .Id("i")
                .Class("c")
                .Style("s")
                .Data(new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<optgroup>&lt;x&gt;</optgroup>", Optgroup["<x>"].ToHtml());
}
