namespace Rask.Core.Tests.Components;

public partial class HeadTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Unset_props_render_only_the_open_and_close_tags() => Assert.Equal("<head></head>", Head.ToHtml());

    [Fact]
    public void Setting_every_prop_emits_the_expected_attributes()
    {
        Assert.Equal(
            "<head id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></head>",
            Head.Id("i").Class("c").Style("s").Data(new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    // Note: Head() is a framework-managed slot. Passing children is a RASK019
    // compile-time error in user code (HeadChildrenAnalyzer). The framework
    // auto-emits the head-asset sentinel inside <head> at render time; user
    // contributions arrive via the Component? Head override and splice through
    // HeadAssetRegistry. The "A_text_child_is_html_encoded" case is therefore
    // intentionally absent — exercising that path with children would mean
    // disabling the analyzer, and the runtime behavior is well-covered by
    // HeadAssetRenderTests against the real framework path.
}
