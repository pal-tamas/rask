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
    // .disabled only greys the item and kills pointer-events — a mouse is stopped, a keyboard is not. So the
    // control also carries aria-disabled, or it would stay focusable while announcing as enabled. It is on the
    // <button> (what takes focus), not the <li>, and it is aria-disabled rather than the disabled attribute so
    // that disabling an item mid-interaction doesn't drop the user's focus to <body>.
    public void PageItem_Disabled_GreysItem_AndSaysSo() =>
        Assert.Equal(
            "<li class=\"page-item disabled\">" +
            "<button class=\"page-link\" aria-disabled=\"true\" type=\"button\">4</button></li>",
            BsPageItem(Disabled: true)["4"].ToHtml());

    [Fact]
    public void PageItem_Disabled_MarksALinkToo() =>
        Assert.Equal(
            "<li class=\"page-item disabled\">" +
            "<a class=\"page-link\" aria-disabled=\"true\" href=\"/p/4\">4</a></li>",
            BsPageItem(Disabled: true, Href: "/p/4")["4"].ToHtml());

    [Fact]
    public void PageItem_Enabled_CarriesNoAriaDisabled() =>
        Assert.DoesNotContain("aria-disabled", BsPageItem()["4"].ToHtml(), StringComparison.Ordinal);

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
