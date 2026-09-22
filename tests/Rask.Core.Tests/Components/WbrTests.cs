namespace Rask.Core.Tests.Components;

public partial class WbrTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Unset_props_render_only_the_self_closing_tag() =>
        Assert.Equal("<wbr />", Wbr.ToHtml());

    [Fact]
    public void Setting_every_prop_emits_the_expected_attributes()
    {
        Assert.Equal("<wbr id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" />",
            Wbr.Id("i").Class("c").Style("s").Data(new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }
}
