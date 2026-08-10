namespace Rask.Core.Tests.Components;

public partial class HeadTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() => Assert.Equal("<head></head>", Head().ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        Assert.Equal(
            "<head id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></head>",
            Head("i", "c", "s", new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    // Note: Head() is a framework-managed slot. Passing children is a RASK019
    // compile-time error in user code (HeadChildrenAnalyzer). The framework
    // auto-emits the head-asset sentinel inside <head> at render time; user
    // contributions arrive via the Component? Head override and splice through
    // HeadAssetRegistry. The "Render_StringChild_EncodesText" case is therefore
    // intentionally absent — exercising that path with children would mean
    // disabling the analyzer, and the runtime behavior is well-covered by
    // HeadAssetRenderTests against the real framework path.
}
