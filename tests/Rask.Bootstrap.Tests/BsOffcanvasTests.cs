namespace Rask.Bootstrap.Tests;

// Rendered-HTML assertions for BsOffcanvas. The panel always stays in the DOM (role="dialog",
// tabindex=-1); Open adds .show and, unless suppressed, a dimming backdrop. ToHtml() renders static
// markup — the click handlers are exercised live in the showcase E2E.
public partial class BsOffcanvasTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Closed_RendersPanelWithHeaderAndBody_NoShowNoBackdrop() =>
        Assert.Equal(
            "<div class=\"offcanvas offcanvas-start\" role=\"dialog\" tabindex=\"-1\">" +
            "<div class=\"offcanvas-header\"><h5 class=\"offcanvas-title\">Menu</h5>" +
            "<button class=\"btn-close\" aria-label=\"Close\" type=\"button\"></button></div>" +
            "<div class=\"offcanvas-body\">Body</div></div>",
            BsOffcanvas.Title("Menu")["Body"].ToHtml());

    [Fact]
    public void Open_AddsShowAndBackdrop() =>
        Assert.Equal(
            "<div class=\"offcanvas offcanvas-start show\" role=\"dialog\" tabindex=\"-1\">" +
            "<div class=\"offcanvas-header\"><h5 class=\"offcanvas-title\">Menu</h5>" +
            "<button class=\"btn-close\" aria-label=\"Close\" type=\"button\"></button></div>" +
            "<div class=\"offcanvas-body\">Body</div></div>" +
            "<div class=\"offcanvas-backdrop fade show\"></div>",
            BsOffcanvas.Open(true).Title("Menu")["Body"].ToHtml());

    [Fact]
    public void Placement_End_SwitchesSideClass() =>
        Assert.Equal(
            "<div class=\"offcanvas offcanvas-end\" role=\"dialog\" tabindex=\"-1\">" +
            "<div class=\"offcanvas-body\">Body</div></div>",
            BsOffcanvas.Placement(BsPlacement.End).HideClose(true)["Body"].ToHtml());

    [Fact]
    // With no Title and HideClose, the drawer carries no header chrome.
    public void HideCloseWithoutTitle_OmitsHeader() =>
        Assert.Equal(
            "<div class=\"offcanvas offcanvas-start\" role=\"dialog\" tabindex=\"-1\">" +
            "<div class=\"offcanvas-body\">Body</div></div>",
            BsOffcanvas.HideClose(true)["Body"].ToHtml());

    [Fact]
    // Backdrop: false suppresses the dimming layer even when open.
    public void OpenWithBackdropFalse_RendersNoBackdrop() =>
        Assert.Equal(
            "<div class=\"offcanvas offcanvas-start show\" role=\"dialog\" tabindex=\"-1\">" +
            "<div class=\"offcanvas-body\">Body</div></div>",
            BsOffcanvas.Open(true).Backdrop(false).HideClose(true)["Body"].ToHtml());
}
