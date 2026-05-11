using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class HgroupTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<hgroup></hgroup>", new Hgroup(null).ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        var props = new Hgroup.Props("i", "c", "s",
            new Dictionary<string, string?> { ["k"] = "v" });
        Assert.Equal("<hgroup id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></hgroup>",
            new Hgroup(props).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<hgroup>&lt;x&gt;</hgroup>", new Hgroup(null, "<x>").ToHtml());
}
