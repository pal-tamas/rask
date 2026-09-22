namespace Rask.Core.Tests.Components;

public partial class AreaTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Unset_props_render_only_the_self_closing_tag() =>
        Assert.Equal("<area />", Area.ToHtml());

    [Fact]
    public void Setting_every_prop_emits_the_expected_attributes()
    {
        Assert.Equal(
            "<area id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" alt=\"alt\" coords=\"0,0,10,10\" shape=\"rect\" href=\"/x\" target=\"_blank\" rel=\"noopener\" download=\"f.zip\" />",
            Area
                .Alt("alt")
                .Coords("0,0,10,10")
                .Shape("rect")
                .Href("/x")
                .Target("_blank")
                .Rel("noopener")
                .Download("f.zip")
                .Id("i")
                .Class("c")
                .Style("s")
                .Data(new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }
}
