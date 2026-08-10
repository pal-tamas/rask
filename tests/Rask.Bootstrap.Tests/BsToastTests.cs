namespace Rask.Bootstrap.Tests;

// Rendered-HTML assertions for BsToast's two layouts (headerless colored vs header). ToHtml() renders
// static markup; the auto-hide timer only starts under a live context (OnMount), so it's inert here.
public partial class BsToastTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Toast_DefaultLayout_RendersHeaderOverBody() =>
        Assert.Equal(
            "<div id=\"2\" class=\"toast show\" role=\"alert\" aria-live=\"assertive\" aria-atomic=\"true\">" +
            "<div class=\"toast-header\">" +
            "<strong class=\"me-auto\">Note</strong>" +
            "<button class=\"btn-close\" aria-label=\"Close\" type=\"button\"></button>" +
            "</div>" +
            "<div class=\"toast-body\">Hi</div>" +
            "</div>",
            BsToast.Id(2).Title("Note").Message("Hi").ToHtml());

    [Fact]
    public void Toast_Colored_RendersHeaderlessColorScheme() =>
        Assert.Equal(
            "<div id=\"1\" class=\"toast show align-items-center text-bg-success border-0\" role=\"alert\" " +
            "aria-live=\"assertive\" aria-atomic=\"true\">" +
            "<div class=\"d-flex\">" +
            "<div class=\"toast-body\">Saved</div>" +
            "<button class=\"btn-close btn-close-white me-2 m-auto\" aria-label=\"Close\" type=\"button\"></button>" +
            "</div>" +
            "</div>",
            BsToast.Id(1).Message("Saved").Color(BsColor.Success).ToHtml());

    [Fact]
    public void Toast_WithTimestamp_AddsSmallText() =>
        Assert.Equal(
            "<div id=\"3\" class=\"toast show\" role=\"alert\" aria-live=\"assertive\" aria-atomic=\"true\">" +
            "<div class=\"toast-header\">" +
            "<strong class=\"me-auto\">Note</strong>" +
            "<small class=\"text-secondary\">now</small>" +
            "<button class=\"btn-close\" aria-label=\"Close\" type=\"button\"></button>" +
            "</div>" +
            "<div class=\"toast-body\">Hi</div>" +
            "</div>",
            BsToast.Id(3).Title("Note").Message("Hi").Timestamp("now").ToHtml());
}
