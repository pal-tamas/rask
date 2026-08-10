namespace Rask.Bootstrap.Tests;

// Rendered-HTML assertions for BsCollapse. ToHtml() renders static markup, which is all these
// structural checks need — the reveal itself is the .show class the live runtime toggles.
public partial class BsCollapseTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Collapse_Closed_HasNoShow() =>
        Assert.Equal(
            "<div class=\"collapse\">Body</div>",
            BsCollapse["Body"].ToHtml());

    [Fact]
    public void Collapse_Open_AddsShow() =>
        Assert.Equal(
            "<div class=\"collapse show\">Body</div>",
            BsCollapse.Open(true)["Body"].ToHtml());

    [Fact]
    public void Collapse_Horizontal_AddsModifier() =>
        Assert.Equal(
            "<div class=\"collapse collapse-horizontal\">Body</div>",
            BsCollapse.Horizontal(true)["Body"].ToHtml());

    [Fact]
    // Modifier order: base, horizontal, show, then the caller's own class.
    public void Collapse_OpenWithUserClass_MergesInOrder() =>
        Assert.Equal(
            "<div class=\"collapse show w-50\">Body</div>",
            BsCollapse.Open(true).Class("w-50")["Body"].ToHtml());
}
