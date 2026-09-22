namespace Rask.Core.Tests.Components;

public partial class TrackTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Unset_props_render_only_the_self_closing_tag() =>
        Assert.Equal("<track />", Track.ToHtml());

    [Fact]
    public void Setting_every_prop_emits_the_expected_attributes()
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
