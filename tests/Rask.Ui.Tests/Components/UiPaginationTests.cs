using System.Text.RegularExpressions;
using Rask.Core.Routing;
using Rask.Testing;

namespace Rask.UiTests.Components;

/// <summary>
///     Flux's pager: what it says about a paginator, which pages it numbers, and its two forms — buttons that
///     report a choice, and links to where each page lives.
/// </summary>
/// <remarks>
///     The windows asserted here are Flux's: its "Large result set" example (67 pages) was widened past 640px
///     and clicked through pages 1–6, 10–12, 59, 60, 63 and 67. Pages 7, 8 and 61 sit on the edges of the rule
///     those show.
/// </remarks>
public partial class UiPaginationTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void The_summary_says_which_results_the_page_shows()
    {
        var paginator = new UiPaginator { Page = 2, PerPage = 15, Total = 240 };

        var html = Ui.Pagination.Paginator(paginator).ToHtml();

        Assert.Contains("Showing 16 to 30 of 240 results", html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_last_page_stops_at_the_last_result()
    {
        var paginator = new UiPaginator { Page = 5, PerPage = 5, Total = 24 };

        var html = Ui.Pagination.Paginator(paginator).ToHtml();

        Assert.Contains("Showing 21 to 24 of 24 results", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_page_past_the_end_is_drawn_as_the_last_page()
    {
        var paginator = new UiPaginator { Page = 9, PerPage = 5, Total = 24 };

        var html = Ui.Pagination.Paginator(paginator).ToHtml();

        Assert.Contains("Showing 21 to 24 of 24 results", html, StringComparison.Ordinal);
        Assert.Contains("aria-current=\"page\">5<", html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_root_carries_the_marker_and_measures_its_own_width()
    {
        var paginator = new UiPaginator { Page = 1, PerPage = 5, Total = 24 };

        var html = Ui.Pagination.Paginator(paginator).ToHtml();

        Assert.StartsWith("<div class=\"@container ", html, StringComparison.Ordinal);
        Assert.Contains(" data-ui-pagination>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Pages_are_buttons_when_the_choice_is_reported()
    {
        var paginator = new UiPaginator { Page = 2, PerPage = 5, Total = 15 };

        var html = Ui.Pagination.Paginator(paginator).OnPage(_ => { }).ToHtml();

        // Previous and Next in the narrow form; Previous, 1, 3 and Next in the numbered one.
        Assert.Equal(6, Count(html, "<button"));
        Assert.DoesNotContain("<a ", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Pressing_a_page_reports_its_number()
    {
        var chosen = 0;
        var page = Page.Render(Ui.Pagination.Paginator(new UiPaginator { Page = 2, PerPage = 5, Total = 24 }).OnPage(p => chosen = p));

        await page.On("button:has-text(\"4\")").Click();

        Assert.Equal(4, chosen);
    }

    [Fact]
    public void Pages_are_links_when_each_page_has_an_address()
    {
        var paginator = new UiPaginator { Page = 2, PerPage = 5, Total = 15 };

        var html = Ui.Pagination.Paginator(paginator).Href(page => $"/logs?page={page}").ToHtml();

        Assert.Contains("href=\"/logs?page=1\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/logs?page=3\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<button", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(10, "Showing 11 to 20 of 95 results", "10")]
    [InlineData(25, "Showing 26 to 50 of 95 results", "4")]
    public void The_page_size_is_the_paginators_and_a_select_beside_the_pager_chooses_it(int perPage, string summary, string lastPage)
    {
        // Flux's pagination has no page-size prop: the choice is a select of the page's own, as docs/ui-kit.md shows.
        var chosen = perPage;
        var page = Div[
            Ui.Select.Value(chosen).OnChange(size => chosen = size).Sm[
                Ui.SelectOption.Value(10)["10"],
                Ui.SelectOption.Value(25)["25"]
            ],
            Ui.Pagination.Paginator(new UiPaginator { Page = 2, PerPage = chosen, Total = 95 }).OnPage(_ => { })
        ];

        var html = page.ToHtml();

        Assert.Contains(summary, html, StringComparison.Ordinal);
        Assert.Contains($">{lastPage}</button><button", html, StringComparison.Ordinal);
        Assert.Contains("<select", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_string_address_is_an_ordinary_link_and_a_generated_route_an_in_app_one()
    {
        var paginator = new UiPaginator { Page = 2, PerPage = 5, Total = 15 };

        var plain = Ui.Pagination.Paginator(paginator).Href(page => $"/logs?page={page}").ToHtml();
        var routed = Ui.Pagination.Paginator(paginator)
            .Href(page => new RouteUrl("/logs", $"?page={page}", typeof(UiPaginationTests)))
            .ToHtml();

        // Previous, 1, 3 and Next in the numbered form and Previous and Next in the narrow one: six links.
        Assert.Equal(6, Count(plain, "<a "));
        Assert.DoesNotContain("data-rask-nav", plain, StringComparison.Ordinal);
        Assert.Equal(6, Count(routed, "data-rask-nav"));
        Assert.Contains("href=\"/logs?page=3\"", routed, StringComparison.Ordinal);
    }

    [Fact]
    public void The_current_page_is_not_a_link_and_says_it_is_the_current_one()
    {
        var paginator = new UiPaginator { Page = 2, PerPage = 5, Total = 15 };

        var html = Ui.Pagination.Paginator(paginator).Href(page => $"/logs?page={page}").ToHtml();

        // Previous leads to page 1 and Next to page 3, in both forms; nothing leads to page 2.
        Assert.DoesNotContain("href=\"/logs?page=2\"", html, StringComparison.Ordinal);
        Assert.Equal(1, Count(html, "aria-current=\"page\""));
        Assert.Contains("aria-current=\"page\">2</div>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void No_page_link_lights_up_as_active_on_its_own()
    {
        // NavLink adds `active` when its URL matches the current one, and a first page with no ?page= in it
        // would match every page of the same path. The pager says which page is current itself.
        var paginator = new UiPaginator { Page = 3, PerPage = 5, Total = 15 };

        var html = Ui.Pagination.Paginator(paginator).Href(page => page == 1 ? "/logs" : $"/logs?page={page}").ToHtml();

        Assert.DoesNotContain(" active", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_link_wins_when_a_handler_is_given_as_well()
    {
        // The client cancels the default action of a click it dispatches, so a page that was both would stop
        // navigating the moment a handler was attached to it.
        var paginator = new UiPaginator { Page = 1, PerPage = 5, Total = 15 };

        var html = Ui.Pagination.Paginator(paginator).OnPage(_ => { }).Href(page => $"/logs?page={page}").ToHtml();

        Assert.DoesNotContain("<button", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/logs?page=2\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void On_the_first_page_Previous_is_not_a_control()
    {
        var paginator = new UiPaginator { Page = 1, PerPage = 5, Total = 24 };

        var html = Ui.Pagination.Paginator(paginator).OnPage(_ => { }).ToHtml().AsText();

        // Once in each form: a <div> that says it is disabled, where Next is a button.
        Assert.Equal(2, Count(html, "<div class=\"flex items-center justify-center rounded-md text-zinc-300 size-8 sm:size-6 dark:text-zinc-500\" data-rask-key=\"previous\" aria-disabled=\"true\" aria-label=\"« Previous\">"));
        Assert.Equal(0, Count(html, "aria-disabled=\"true\" aria-label=\"Next »\""));
        Assert.Equal(2, Count(html, "aria-label=\"Next »\""));
    }

    [Fact]
    public void An_arrow_is_named_with_the_character_and_not_with_the_name_of_its_entity()
    {
        // "&amp;laquo; Previous" in the markup is "&laquo; Previous" to a screen reader.
        var paginator = new UiPaginator { Page = 2, PerPage = 5, Total = 24 };

        var html = Ui.Pagination.Paginator(paginator).OnPage(_ => { }).ToHtml();

        var labels = Regex.Matches(html, "aria-label=\"([^\"]*)\"").Select(label => label.Groups[1].Value).ToList();
        Assert.Equal(4, labels.Count);
        Assert.All(labels, label => Assert.DoesNotContain("&amp;", label, StringComparison.Ordinal));
        Assert.Equal(["« Previous", "Next »"], labels.Select(label => label.AsText()).Distinct(StringComparer.Ordinal));
    }

    [Fact]
    public void On_the_last_page_Next_is_not_a_control()
    {
        var paginator = new UiPaginator { Page = 5, PerPage = 5, Total = 24 };

        var html = Ui.Pagination.Paginator(paginator).OnPage(_ => { }).ToHtml().AsText();

        Assert.Equal(2, Count(html, "aria-disabled=\"true\" aria-label=\"Next »\""));
        Assert.Equal(0, Count(html, "aria-disabled=\"true\" aria-label=\"« Previous\""));
    }

    [Theory]
    [InlineData(1, "[1] 2 3 4 5 6 7 8 9 10 ... 66 67")]
    [InlineData(6, "1 2 3 4 5 [6] 7 8 9 10 ... 66 67")]
    [InlineData(7, "1 2 3 4 5 6 [7] 8 9 10 ... 66 67")]
    [InlineData(8, "1 2 ... 5 6 7 [8] 9 10 11 ... 66 67")]
    [InlineData(10, "1 2 ... 7 8 9 [10] 11 12 13 ... 66 67")]
    [InlineData(60, "1 2 ... 57 58 59 [60] 61 62 63 ... 66 67")]
    [InlineData(61, "1 2 ... 58 59 60 [61] 62 63 64 65 66 67")]
    [InlineData(63, "1 2 ... 58 59 60 61 62 [63] 64 65 66 67")]
    [InlineData(67, "1 2 ... 58 59 60 61 62 63 64 65 66 [67]")]
    public void A_long_list_numbers_a_window_of_pages_as_Flux_does(int current, string expected)
    {
        var paginator = new UiPaginator { Page = current, PerPage = 15, Total = 1000 };

        var html = Ui.Pagination.Paginator(paginator).OnPage(_ => { }).ToHtml();

        Assert.Equal(expected, Drawn(html));
    }

    [Fact]
    public void Thirteen_pages_are_all_numbered_and_fourteen_open_a_gap()
    {
        var thirteen = new UiPaginator { Page = 1, PerPage = 10, Total = 130 };
        var fourteen = new UiPaginator { Page = 1, PerPage = 10, Total = 140 };

        var all = Ui.Pagination.Paginator(thirteen).OnPage(_ => { }).ToHtml();
        var windowed = Ui.Pagination.Paginator(fourteen).OnPage(_ => { }).ToHtml();

        Assert.Equal("[1] 2 3 4 5 6 7 8 9 10 11 12 13", Drawn(all));
        Assert.Equal("[1] 2 3 4 5 6 7 8 9 10 ... 13 14", Drawn(windowed));
    }

    [Fact]
    public void A_gap_is_not_a_control()
    {
        var paginator = new UiPaginator { Page = 30, PerPage = 15, Total = 1000 };

        var html = Ui.Pagination.Paginator(paginator).OnPage(_ => { }).ToHtml();

        Assert.Equal(2, Count(html, "aria-disabled=\"true\">...</div>"));
    }

    [Fact]
    public void A_paginator_with_no_total_draws_only_Previous_and_Next()
    {
        var paginator = new UiPaginator { Page = 2, HasMore = true };

        var html = Ui.Pagination.Paginator(paginator).OnPage(_ => { }).ToHtml();

        Assert.DoesNotContain("Showing", html, StringComparison.Ordinal);
        Assert.DoesNotContain("@container", html, StringComparison.Ordinal);
        Assert.Equal(2, Count(html, "<button"));
        Assert.Equal("", Drawn(html));
    }

    [Fact]
    public void A_simple_paginator_with_nothing_more_ends_there()
    {
        var paginator = new UiPaginator { Page = 2, HasMore = false };

        var html = Ui.Pagination.Paginator(paginator).OnPage(_ => { }).ToHtml();

        // Flux's simple pager names nothing and states nothing: the spent step is a bare box.
        Assert.Equal(1, Count(html, "dark:text-zinc-500\" data-rask-key=\"next\"><svg"));
        Assert.Equal(1, Count(html, "<button"));
        Assert.DoesNotContain("aria-", html.Replace("aria-hidden", "", StringComparison.Ordinal), StringComparison.Ordinal);
    }

    [Fact]
    public void ScrollTo_names_what_the_runtime_brings_into_view_on_a_press()
    {
        var paginator = new UiPaginator { Page = 1, PerPage = 5, Total = 24 };

        var scrolling = Ui.Pagination.Paginator(paginator).ScrollTo("#orders").ToHtml();
        var still = Ui.Pagination.Paginator(paginator).ToHtml();

        Assert.Contains("data-rask-scroll-to=\"#orders\"", scrolling, StringComparison.Ordinal);
        Assert.DoesNotContain("data-rask-scroll-to", still, StringComparison.Ordinal);
    }

    [Fact]
    public void Each_arrow_is_drawn_twice_so_it_can_turn_round_in_a_right_to_left_page()
    {
        var paginator = new UiPaginator { Page = 1, HasMore = true };

        var html = Ui.Pagination.Paginator(paginator).ToHtml();

        Assert.Equal(2, Count(html, "rtl:hidden"));
        Assert.Equal(2, Count(html, "hidden rtl:inline"));
    }

    // The numbered form's pages in order: the current one in brackets, a gap as "...".
    private static string Drawn(string html) => string.Join(' ', Regex.Matches(html, "(aria-current=\"page\")?>([0-9]+|\\.\\.\\.)</(?:div|button|a)>")
        .Select(m => m.Groups[1].Success ? $"[{m.Groups[2].Value}]" : m.Groups[2].Value));

    private static int Count(string haystack, string needle) => haystack.Split(needle).Length - 1;
}
