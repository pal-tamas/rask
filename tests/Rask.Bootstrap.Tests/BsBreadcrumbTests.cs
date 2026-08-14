namespace Rask.Bootstrap.Tests;

// Rendered-HTML assertions for BsBreadcrumb / BsBreadcrumbItem.
public partial class BsBreadcrumbTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Breadcrumb_WrapsItemsInNavAndOl() =>
        Assert.Equal(
            "<nav aria-label=\"breadcrumb\"><ol class=\"breadcrumb\">" +
            "<li class=\"breadcrumb-item\"><a href=\"/home\">Home</a></li>" +
            "</ol></nav>",
            BsBreadcrumb[BsBreadcrumbItem.Href("/home")["Home"]].ToHtml());

    [Fact]
    public void Breadcrumb_CustomLabel_OverridesNavAria() =>
        Assert.Equal(
            "<nav aria-label=\"Docs sections\"><ol class=\"breadcrumb\"></ol></nav>",
            BsBreadcrumb.Label("Docs sections").ToHtml());

    [Fact]
    // The current page carries no link — .active greys it and aria-current="page" announces it.
    public void BreadcrumbItem_Active_IsPlainTextWithAriaCurrent() =>
        Assert.Equal(
            "<li class=\"breadcrumb-item active\" aria-current=\"page\">Data</li>",
            BsBreadcrumbItem.Active(true)["Data"].ToHtml());

    [Fact]
    // Active wins over Href: an active item never renders the anchor even when a link is supplied.
    public void BreadcrumbItem_ActiveWithHref_StaysPlainText() =>
        Assert.Equal(
            "<li class=\"breadcrumb-item active\" aria-current=\"page\">Data</li>",
            BsBreadcrumbItem.Active(true).Href("/data")["Data"].ToHtml());

    [Fact]
    public void BreadcrumbItem_Href_WrapsAnchor() =>
        Assert.Equal(
            "<li class=\"breadcrumb-item\"><a href=\"/library\">Library</a></li>",
            BsBreadcrumbItem.Href("/library")["Library"].ToHtml());

    [Fact]
    public void BreadcrumbItem_MergesUserClass() =>
        Assert.Equal(
            "<li class=\"breadcrumb-item fw-bold\">Plain</li>",
            BsBreadcrumbItem.Class("fw-bold")["Plain"].ToHtml());
}
