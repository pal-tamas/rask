namespace Rask.Core.Tests.Components;

public partial class IframeTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Unset_props_render_only_the_open_and_close_tags() =>
        Assert.Equal("<iframe></iframe>", Iframe.ToHtml());

    [Fact]
    public void Setting_every_prop_emits_the_expected_attributes()
    {
        Assert.Equal(
            "<iframe id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" src=\"/page\" srcdoc=\"&lt;p&gt;x&lt;/p&gt;\" name=\"n\" sandbox=\"allow-scripts\" allow=\"camera\" width=\"640\" height=\"480\" loading=\"lazy\" referrerpolicy=\"no-referrer\"></iframe>",
            Iframe
                .Src("/page")
                .Srcdoc("<p>x</p>")
                .Name("n")
                .Sandbox("allow-scripts")
                .Allow("camera")
                .Width(640)
                .Height(480)
                .Loading("lazy")
                .ReferrerPolicy("no-referrer")
                .Id("i")
                .Class("c")
                .Style("s")
                .Data(new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void A_text_child_is_html_encoded() =>
        Assert.Equal("<iframe>&lt;x&gt;</iframe>", Iframe["<x>"].ToHtml());

    [Fact]
    public void Fetch_priority_comes_after_the_other_iframe_attributes() =>
        Assert.Equal("<iframe src=\"/a\" loading=\"lazy\" fetchpriority=\"low\"></iframe>",
            Iframe.Src("/a").Loading("lazy").FetchPriority("low").ToHtml());
}
