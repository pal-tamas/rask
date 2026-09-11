namespace Rask.Ui.Tests.Components;

/// <summary>
///     The pager, in both of its forms: buttons that report a choice, and links to where each page lives.
/// </summary>
public partial class UiPaginationTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Pages_are_buttons_when_the_choice_is_reported()
    {
        var html = UiPagination.Pages(3).Current(2).OnSelect(_ => { }).ToHtml();

        Assert.Equal(3, Count(html, "<button"));
        Assert.DoesNotContain("<a ", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Pages_are_links_when_each_page_has_an_address()
    {
        var html = UiPagination.Pages(3).Current(2).Href(page => $"/logs?page={page}").ToHtml();

        Assert.Contains("href=\"/logs?page=1\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/logs?page=3\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<button", html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_current_page_is_not_a_link_and_says_it_is_the_current_one()
    {
        var html = UiPagination.Pages(3).Current(2).Href(page => $"/logs?page={page}").ToHtml();

        Assert.DoesNotContain("href=\"/logs?page=2\"", html, StringComparison.Ordinal);
        Assert.Equal(1, Count(html, "aria-current=\"page\""));
        Assert.Contains("btn-active", html, StringComparison.Ordinal);
    }

    [Fact]
    public void No_page_link_lights_up_as_active_on_its_own()
    {
        // NavLink adds `active` when its URL matches the current one, and a first page with no ?page= in it
        // would match every page of the same path. The pager says which page is current itself.
        var html = UiPagination.Pages(3).Current(3).Href(page => page == 1 ? "/logs" : $"/logs?page={page}").ToHtml();

        Assert.DoesNotContain(" active", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_link_wins_when_a_handler_is_given_as_well()
    {
        // The client cancels the default action of a click it dispatches, so a page that was both would stop
        // navigating the moment a handler was attached to it.
        var html = UiPagination.Pages(3).Current(1).OnSelect(_ => { }).Href(page => $"/logs?page={page}").ToHtml();

        Assert.DoesNotContain("<button", html, StringComparison.Ordinal);
        Assert.Equal(2, Count(html, "<a "));
    }

    private static int Count(string haystack, string needle) => haystack.Split(needle).Length - 1;
}
