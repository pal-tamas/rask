namespace Rask.Core.Tests.Components;

public partial class SourceTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Unset_props_render_only_the_self_closing_tag() =>
        Assert.Equal("<source />", Source.ToHtml());

    [Fact]
    public void Setting_every_prop_emits_the_expected_attributes()
    {
        Assert.Equal(
            "<source id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" src=\"/a.mp4\" type=\"video/mp4\" srcset=\"/a.png 1x\" sizes=\"100vw\" media=\"(min-width:600px)\" />",
            Source
                .Src("/a.mp4")
                .Type("video/mp4")
                .Srcset("/a.png 1x")
                .Sizes("100vw")
                .Media("(min-width:600px)")
                .Id("i")
                .Class("c")
                .Style("s")
                .Data(new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }
}
