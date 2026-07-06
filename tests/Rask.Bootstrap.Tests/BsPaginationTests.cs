namespace Rask.Bootstrap.Tests;

// Rendered-HTML assertions for BsPagination / BsPageItem.
public class BsPaginationTests
{
    [Fact]
    public void Pagination_WrapsItemsInNavAndUl() =>
        Assert.Equal(
            "<nav aria-label=\"Page navigation\"><ul class=\"pagination\">" +
            "<li class=\"page-item\"><a class=\"page-link\" href=\"/page/2\">2</a></li>" +
            "</ul></nav>",
            BsPagination()[BsPageItem(Href: "/page/2")["2"]].ToHtml());

    [Fact]
    public void PageItem_Active_MarksCurrentPage() =>
        Assert.Equal(
            "<li class=\"page-item active\" aria-current=\"page\">" +
            "<button class=\"page-link\" type=\"button\">3</button></li>",
            BsPageItem(Active: true)["3"].ToHtml());

    [Fact]
    public void PageItem_Disabled_GreysItem() =>
        Assert.Equal(
            "<li class=\"page-item disabled\">" +
            "<button class=\"page-link\" type=\"button\">4</button></li>",
            BsPageItem(Disabled: true)["4"].ToHtml());

    [Fact]
    public void Pagination_Size_MapsToPaginationModifier() =>
        Assert.Equal(
            "<nav aria-label=\"Page navigation\"><ul class=\"pagination pagination-lg\"></ul></nav>",
            BsPagination(Size: BsSize.Lg).ToHtml());

    [Fact]
    public void Pagination_CustomLabel_OverridesNavAria() =>
        Assert.Equal(
            "<nav aria-label=\"Results\"><ul class=\"pagination\"></ul></nav>",
            BsPagination(Label: "Results").ToHtml());
}
