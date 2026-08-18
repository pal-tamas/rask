namespace Rask.Html.Tests.Components;

public partial class LinkTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Render_NullProps_ReturnsSelfClosingTag() => Assert.Equal("<link />", Link.ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
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
    public void Render_FetchPriorityAndBlocking_EmitAfterTheOtherLinkAttrs() =>
        Assert.Equal(
            "<link href=\"/a.css\" rel=\"stylesheet\" fetchpriority=\"high\" blocking=\"render\" />",
            Link.Rel("stylesheet").Href("/a.css").FetchPriority("high").Blocking("render").ToHtml());

    [Fact]
    public void Render_ImagePreloadCarriesItsOwnSrcsetAndSizes() =>
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
