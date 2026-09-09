namespace Rask.Core.Tests.Components;

public partial class STests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<s></s>", S.ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        Assert.Equal("<s id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></s>",
            S.Id("i").Class("c").Style("s").Data(new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<s>&lt;x&gt;</s>", S["<x>"].ToHtml());
}
