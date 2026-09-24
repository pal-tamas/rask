namespace Rask.Core.Tests.Components;

public partial class BaseTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Unset_props_render_a_self_closing_tag() => Assert.Equal("<base />", Base.ToHtml());

    [Fact]
    public void Setting_every_prop_emits_the_expected_attributes()
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
