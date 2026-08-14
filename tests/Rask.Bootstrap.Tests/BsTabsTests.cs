namespace Rask.Bootstrap.Tests;

// Rendered-HTML assertions for BsTabs. Only the active pane is rendered (live-runtime driven, no JS).
// Each nav <li> carries the tab Key as data-rask-key (reconciliation identity).
public partial class BsTabsTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Tabs_RendersNavAndActivePaneOnly() =>
        Assert.Equal(
            "<div>" +
            "<ul class=\"nav nav-tabs\" role=\"tablist\">" +
            "<li class=\"nav-item\" data-rask-key=\"a\" role=\"presentation\">" +
            "<button class=\"nav-link active\" role=\"tab\" aria-selected=\"true\" type=\"button\">Tab A</button></li>" +
            "<li class=\"nav-item\" data-rask-key=\"b\" role=\"presentation\">" +
            "<button class=\"nav-link\" role=\"tab\" aria-selected=\"false\" type=\"button\">Tab B</button></li>" +
            "</ul>" +
            "<div class=\"tab-content\"><div class=\"tab-pane show active\" role=\"tabpanel\">Pane A</div></div>" +
            "</div>",
            BsTabs
                .Tabs([new BsTabItem("a", "Tab A", "Pane A"), new BsTabItem("b", "Tab B", "Pane B")])
                .Active("a").ToHtml());

    [Fact]
    public void Tabs_Pills_UsesPillsVariant() =>
        Assert.Equal(
            "<div>" +
            "<ul class=\"nav nav-pills\" role=\"tablist\">" +
            "<li class=\"nav-item\" data-rask-key=\"k\" role=\"presentation\">" +
            "<button class=\"nav-link active\" role=\"tab\" aria-selected=\"true\" type=\"button\">Only</button></li>" +
            "</ul>" +
            "<div class=\"tab-content\"><div class=\"tab-pane show active\" role=\"tabpanel\">P</div></div>" +
            "</div>",
            BsTabs.Tabs([new BsTabItem("k", "Only", "P")]).Active("k").Pills(true).ToHtml());

    [Fact]
    public void Tab_Disabled_AddsDisabledClass() =>
        Assert.Equal(
            "<div>" +
            "<ul class=\"nav nav-tabs\" role=\"tablist\">" +
            "<li class=\"nav-item\" data-rask-key=\"x\" role=\"presentation\">" +
            "<button class=\"nav-link disabled\" role=\"tab\" aria-selected=\"false\" type=\"button\">Off</button></li>" +
            "</ul>" +
            "<div class=\"tab-content\"></div>" +
            "</div>",
            BsTabs.Tabs([new BsTabItem("x", "Off", "hidden", Disabled: true)]).Active("other").ToHtml());
}
