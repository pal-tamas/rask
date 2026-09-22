namespace Rask.Core.Tests.Components;

public partial class BrTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Unset_props_render_only_the_self_closing_tag() => Assert.Equal("<br />", Br.ToHtml());

    [Fact]
    public void Setting_every_prop_emits_the_expected_attributes()
    {
        Assert.Equal(
            "<br id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" />",
            Br.Id("i").Class("c").Style("s").Data(new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }
}
