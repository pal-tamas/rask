namespace Rask.Html.Tests.Components;

public partial class DataTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<data></data>", Data.ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        Assert.Equal("<data id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" value=\"42\"></data>",
            Data.Value("42").Id("i").Class("c").Style("s").Data(new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<data>&lt;x&gt;</data>", Data["<x>"].ToHtml());
}
