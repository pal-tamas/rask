namespace Rask.Core.Tests.Components;

public partial class MetaTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Unset_props_render_only_the_self_closing_tag() => Assert.Equal("<meta />", Meta.ToHtml());

    [Fact]
    public void Setting_every_prop_emits_the_expected_attributes()
    {
        Assert.Equal(
            "<meta id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" charset=\"utf-8\" name=\"viewport\" content=\"width=device-width\" http-equiv=\"X-UA-Compatible\" />",
            Meta
                .Charset("utf-8")
                .Name("viewport")
                .Content("width=device-width")
                .HttpEquiv("X-UA-Compatible")
                .Id("i")
                .Class("c")
                .Style("s")
                .Data(new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }
}
