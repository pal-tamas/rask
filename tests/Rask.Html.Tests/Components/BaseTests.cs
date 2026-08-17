namespace Rask.Html.Tests.Components;

public partial class BaseTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Render_NullProps_ReturnsSelfClosingTag() => Assert.Equal("<base />", Base.ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        Assert.Equal(
            "<base id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" href=\"/\" target=\"_blank\" />",
            Base
                .Href("/")
                .Target("_blank")
                .Id("i")
                .Class("c")
                .Style("s")
                .Data(new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }
}
