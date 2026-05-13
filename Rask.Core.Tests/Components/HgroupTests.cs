using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class HgroupTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<hgroup></hgroup>", Hgroup().ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
                Assert.Equal("<hgroup id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></hgroup>",
            Hgroup(Id: "i", Class: "c", Style: "s", Data: new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<hgroup>&lt;x&gt;</hgroup>", Hgroup()["<x>"].ToHtml());
}
