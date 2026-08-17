namespace Rask.Html.Tests.Components;

public partial class TrackTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Render_NullProps_ReturnsSelfClosingTag() =>
        Assert.Equal("<track />", Track.ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        Assert.Equal(
            "<track id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" kind=\"subtitles\" src=\"/sub.vtt\" srclang=\"en\" label=\"English\" default />",
            Track
                .Kind("subtitles")
                .Src("/sub.vtt")
                .Srclang("en")
                .Label("English")
                .Default(true)
                .Id("i")
                .Class("c")
                .Style("s")
                .Data(new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }
}
