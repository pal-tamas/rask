namespace Rask.Core.Tests.Components;

public partial class TfootTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<tfoot></tfoot>", Tfoot.ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        Assert.Equal("<tfoot id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></tfoot>",
            Tfoot.Id("i").Class("c").Style("s").Data(new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<tfoot>&lt;x&gt;</tfoot>", Tfoot["<x>"].ToHtml());
}
