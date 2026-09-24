namespace Rask.Core.Tests.Components;

public partial class LinkTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Unset_props_render_a_self_closing_tag() => Assert.Equal("<link />", Link.ToHtml());

    [Fact]
    public void Setting_every_prop_emits_the_expected_attributes()
    {
        Assert.Equal(
            "<link id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" href=\"/style.css\" rel=\"stylesheet\" type=\"text/css\" media=\"all\" sizes=\"16x16\" hreflang=\"en\" as=\"style\" crossorigin=\"anonymous\" referrerpolicy=\"no-referrer\" disabled color=\"#fff\" />",
            Link
                .Href("/style.css")
                .Rel("stylesheet")
                .Type("text/css")
                .Media("all")
                .Sizes("16x16")
                .Hreflang("en")
                .As("style")
                .CrossOrigin("anonymous")
                .ReferrerPolicy("no-referrer")
                .Disabled(true)
                .Color("#fff")
                .Id("i")
                .Class("c")
                .Style("s")
                .Data(new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void Fetch_priority_and_blocking_come_after_the_other_link_attributes() =>
        Assert.Equal(
            "<link href=\"/a.css\" rel=\"stylesheet\" fetchpriority=\"high\" blocking=\"render\" />",
            Link.Rel("stylesheet").Href("/a.css").FetchPriority("high").Blocking("render").ToHtml());

    [Fact]
    public void An_image_preload_carries_its_own_srcset_and_sizes() =>
        // Preloading a responsive image without these fetches the wrong candidate and the page pays for
        // two downloads — the opposite of what the preload was for.
        Assert.Equal(
            "<link href=\"/a.png\" rel=\"preload\" as=\"image\" "
            + "imagesrcset=\"/a.png 1x, /a@2x.png 2x\" imagesizes=\"100vw\" />",
            Link
                .Rel("preload")
                .Href("/a.png")
                .As("image")
                .ImageSrcset("/a.png 1x, /a@2x.png 2x")
                .ImageSizes("100vw").ToHtml());
}
