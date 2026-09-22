#pragma warning disable RASK023 // null-props test deliberately omits Alt to assert bare rendering

namespace Rask.Core.Tests.Components;

public partial class ImgTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Unset_props_render_only_the_self_closing_tag() => Assert.Equal("<img />", Img.ToHtml());

    [Fact]
    public void Setting_every_prop_emits_the_expected_attributes()
    {
        Assert.Equal(
            "<img id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" src=\"/a.png\" alt=\"alt text\" width=\"100\" height=\"50\" loading=\"lazy\" srcset=\"/a.png 1x, /a@2x.png 2x\" sizes=\"100vw\" crossorigin=\"anonymous\" referrerpolicy=\"no-referrer\" decoding=\"async\" usemap=\"#m\" ismap />",
            Img
                .Src("/a.png")
                .Alt("alt text")
                .Width(100)
                .Height(50)
                .Loading("lazy")
                .Srcset("/a.png 1x, /a@2x.png 2x")
                .Sizes("100vw")
                .CrossOrigin("anonymous")
                .ReferrerPolicy("no-referrer")
                .Decoding("async")
                .UseMap("#m")
                .Ismap(true)
                .Id("i")
                .Class("c")
                .Style("s")
                .Data(new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void Fetch_priority_emits_after_the_other_img_attributes() =>
        // `high` on the LCP image is the one use with a measurable story: the browser discovers it at
        // the same moment either way, this just moves it ahead in the queue.
        Assert.Equal("<img src=\"/hero.png\" alt=\"Hero\" fetchpriority=\"high\" />",
            Img.Src("/hero.png").Alt("Hero").FetchPriority("high").ToHtml());
}
