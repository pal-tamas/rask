using System.Text.RegularExpressions;
using Rask.Testing;

namespace Rask.UiTests.Components;

/// <summary>
///     The menu's keyboard cursor, its submenus and its checkable rows, driven through the real handlers.
/// </summary>
/// <remarks>
///     <para>
///     Every expectation here was read off Flux UI's live dropdown page, key by key: where focus is after
///     opening, what each arrow does at an end, that Home and End do nothing, what Enter and Space each do on a
///     submenu's row.
///     </para>
///     <para>
///     <c>Test</c> dispatches in-process and re-renders, so none of this needs a browser. What does — the
///     popover actually opening, focus following the cursor, the safe triangle — is the site's browser suite's.
///     </para>
/// </remarks>
public partial class UiDropdownInteractionTests : global::Rask.Core.RaskMarkup
{
    private sealed class View
    {
        public bool ShowArchived { get; set; }

        public string Sort { get; set; } = "name";
    }

    private static global::Rask.Core.Component Menu(View? view = null, List<string>? log = null, bool keepOpen = false)
    {
        view ??= new View();
        return Ui.Dropdown[
            Button["Actions"],
            Ui.Menu.KeepOpen(keepOpen)[
                Ui.MenuItem.Key("edit").OnClick(() => log?.Add("edit"))["Edit"],
                Ui.MenuItem.Key("dup").Disabled(true)["Duplicate"],
                Ui.MenuSubmenu.Key("sort").Heading("Sort by")[
                    Ui.MenuRadioGroup.Bind(() => view.Sort).Key("sort-group")[
                        Ui.MenuRadio.Key("name").Value("name")["Name"],
                        Ui.MenuRadio.Key("date").Value("date")["Date"]
                    ]
                ],
                Ui.MenuSeparator.Key("sep"),
                Ui.MenuCheckbox.Bind(() => view.ShowArchived).Key("archived")["Show archived"],
                Ui.MenuItem.Key("delete").Danger["Delete"]
            ]
        ];
    }

    // The row carrying data-active, read back as its words: the row the cursor is on, and the one with the
    // roving tab stop — which must be the same row, and the only one.
    private static string Cursor(string html)
    {
        var rows = Regex.Matches(html, "<button[^>]*data-active=\"\"[^>]*>(.*?)</button>", RegexOptions.Singleline);
        if (rows.Count == 0)
        {
            Assert.DoesNotContain("tabindex=\"0\"", html, StringComparison.Ordinal);
            return "";
        }

        var row = Assert.Single(rows);
        Assert.Contains("tabindex=\"0\"", row.Value, StringComparison.Ordinal);
        return Regex.Replace(row.Groups[1].Value, "<[^>]+>", "").Trim();
    }

    private static Task OpenAsync(Page page) =>
        page.On("[popover]").Raise("toggle", "{\"oldState\":\"closed\",\"newState\":\"open\"}");

    private static Task KeyAsync(Page page, string key) =>
        page.On("[popover]").Raise("keydown", $"{{\"key\":\"{key}\"}}");

    [Fact]
    public async Task Opening_leaves_the_cursor_nowhere_and_the_trigger_says_it_is_open()
    {
        var page = Page.Render(Menu());

        await OpenAsync(page);

        Assert.Equal("", Cursor(page.Html));
        Assert.Contains("aria-expanded=\"true\"", page.Html, StringComparison.Ordinal);
        Assert.Matches("<div[^>]*data-ui-dropdown=\"\"[^>]*data-open=\"\"", page.Html);
        Assert.Matches("<button[^>]*data-open=\"\"[^>]*popovertarget", page.Html);
    }

    [Theory]
    [InlineData("ArrowDown")]
    [InlineData("ArrowUp")]
    public async Task Either_arrow_starts_at_the_first_row(string key)
    {
        var page = Page.Render(Menu());
        await OpenAsync(page);

        await KeyAsync(page, key);

        Assert.Equal("Edit", Cursor(page.Html));
    }

    [Fact]
    public async Task The_arrows_step_over_what_is_disabled_and_stop_at_the_ends()
    {
        var page = Page.Render(Menu());
        await OpenAsync(page);
        await KeyAsync(page, "ArrowDown");

        await KeyAsync(page, "ArrowDown");
        var afterDisabled = Cursor(page.Html);
        await KeyAsync(page, "ArrowDown");
        await KeyAsync(page, "ArrowDown");
        await KeyAsync(page, "ArrowDown");
        var atTheBottom = Cursor(page.Html);
        await KeyAsync(page, "ArrowUp");
        await KeyAsync(page, "ArrowUp");
        await KeyAsync(page, "ArrowUp");
        await KeyAsync(page, "ArrowUp");

        // "Duplicate" is disabled: the cursor steps over it. And there is no wrapping.
        Assert.Equal("Sort by", afterDisabled);
        Assert.Equal("Delete", atTheBottom);
        Assert.Equal("Edit", Cursor(page.Html));
    }

    [Theory]
    [InlineData("Home")]
    [InlineData("End")]
    [InlineData("PageUp")]
    [InlineData("PageDown")]
    public async Task Home_End_and_the_page_keys_do_not_move_the_cursor(string key)
    {
        var page = Page.Render(Menu());
        await OpenAsync(page);
        await KeyAsync(page, "ArrowDown");
        await KeyAsync(page, "ArrowDown");

        await KeyAsync(page, key);

        Assert.Equal("Sort by", Cursor(page.Html));
    }

    [Fact]
    public async Task ArrowRight_opens_a_submenu_onto_its_first_row_and_ArrowLeft_closes_it()
    {
        var page = Page.Render(Menu());
        await OpenAsync(page);
        await KeyAsync(page, "ArrowDown");
        await KeyAsync(page, "ArrowDown");

        await KeyAsync(page, "ArrowRight");
        var opened = page.Html;
        await KeyAsync(page, "ArrowDown");
        await KeyAsync(page, "ArrowDown");
        var atItsEnd = Cursor(page.Html);
        await KeyAsync(page, "ArrowLeft");

        Assert.Matches("<div[^>]*data-ui-menu-submenu=\"\"[^>]*data-open=\"\"", opened);
        Assert.Equal("Name", Cursor(opened));
        // Its own level, and no wrapping there either.
        Assert.Equal("Date", atItsEnd);
        Assert.DoesNotMatch("<div[^>]*data-ui-menu-submenu=\"\"[^>]*data-open=\"\"", page.Html);
        Assert.Equal("Sort by", Cursor(page.Html));
    }

    [Fact]
    public async Task Enter_on_a_submenu_row_opens_it_onto_its_first_row()
    {
        var page = Page.Render(Menu());
        await OpenAsync(page);
        await KeyAsync(page, "ArrowDown");
        await KeyAsync(page, "ArrowDown");

        await KeyAsync(page, "Enter");

        Assert.Matches("<div[^>]*data-ui-menu-submenu=\"\"[^>]*data-open=\"\"", page.Html);
        Assert.Equal("Name", Cursor(page.Html));
    }

    [Fact]
    public async Task Space_on_a_submenu_row_opens_it_and_leaves_the_cursor_on_the_row()
    {
        var page = Page.Render(Menu());
        await OpenAsync(page);
        await KeyAsync(page, "ArrowDown");
        await KeyAsync(page, "ArrowDown");

        await KeyAsync(page, " ");

        Assert.Matches("<div[^>]*data-ui-menu-submenu=\"\"[^>]*data-open=\"\"", page.Html);
        Assert.Equal("Sort by", Cursor(page.Html));
    }

    [Fact]
    public async Task ArrowRight_on_an_ordinary_row_and_ArrowLeft_at_the_top_level_do_nothing()
    {
        var page = Page.Render(Menu());
        await OpenAsync(page);
        await KeyAsync(page, "ArrowDown");

        await KeyAsync(page, "ArrowRight");
        await KeyAsync(page, "ArrowLeft");

        Assert.Equal("Edit", Cursor(page.Html));
        Assert.DoesNotMatch("<div[^>]*data-ui-menu-submenu=\"\"[^>]*data-open=\"\"", page.Html);
    }

    [Fact]
    public async Task Typing_a_letter_jumps_to_the_row_that_starts_with_it()
    {
        var page = Page.Render(Menu());
        await OpenAsync(page);

        await KeyAsync(page, "d");
        var first = Cursor(page.Html);
        var other = Page.Render(Menu());
        await OpenAsync(other);
        await KeyAsync(other, "s");
        await KeyAsync(other, "h");

        // "Duplicate" starts with d too, and cannot be picked.
        Assert.Equal("Delete", first);
        Assert.Equal("Show archived", Cursor(other.Html));
    }

    [Fact]
    public async Task A_modified_key_is_left_to_the_browser()
    {
        var page = Page.Render(Menu());
        await OpenAsync(page);

        await page.On("[popover]").Raise("keydown", "{\"key\":\"ArrowDown\",\"ctrlKey\":true}");

        Assert.Equal("", Cursor(page.Html));
    }

    [Fact]
    public async Task A_checkbox_row_writes_to_its_model_and_a_radio_row_to_its_group()
    {
        var view = new View();
        var page = Page.Render(Menu(view));
        await OpenAsync(page);

        await page.On("[role=\"menuitemcheckbox\"]").Click();
        var date = Regex.Match(page.Html, "<button id=\"([^\"]+)\"[^>]*role=\"menuitemradio\"[^>]*>(?:(?!</button>).)*Date", RegexOptions.Singleline).Groups[1].Value;
        await page.On("#" + date).Click();

        Assert.True(view.ShowArchived);
        Assert.Equal("date", view.Sort);
        Assert.Matches("<button[^>]*data-checked=\"\"[^>]*role=\"menuitemcheckbox\"[^>]*aria-checked=\"true\"", page.Html);
        Assert.Matches("<button id=\"" + Regex.Escape(date) + "\"[^>]*aria-checked=\"true\"", page.Html);
        Assert.Single(Regex.Matches(page.Html, "role=\"menuitemradio\"[^>]*aria-checked=\"true\""));
    }

    [Fact]
    public async Task A_controlled_checkbox_and_radio_group_report_the_pick_and_keep_what_they_were_given()
    {
        var picked = new List<string>();
        var page = Page.Render(Ui.Dropdown[
            Button["Filter"],
            Ui.Menu[
                Ui.MenuCheckbox.Value(false).OnChange(on => picked.Add("draft=" + on))["Draft"],
                Ui.MenuRadioGroup.Value("name").OnChange(sort => picked.Add("sort=" + sort))[
                    Ui.MenuRadio.Value("name")["Name"],
                    Ui.MenuRadio.Value("date").OnClick(() => picked.Add("clicked"))["Date"]
                ]
            ]
        ]);
        await OpenAsync(page);

        await page.On("[role=\"menuitemcheckbox\"]").Click();
        var date = Regex.Match(page.Html, "<button id=\"([^\"]+)\"[^>]*role=\"menuitemradio\"[^>]*>(?:(?!</button>).)*Date", RegexOptions.Singleline).Groups[1].Value;
        await page.On("#" + date).Click();

        Assert.Equal(["draft=True", "sort=date", "clicked"], picked);
        Assert.Matches("role=\"menuitemcheckbox\"[^>]*aria-checked=\"false\"", page.Html);
        Assert.Matches("<button id=\"" + Regex.Escape(date) + "\"[^>]*aria-checked=\"false\"", page.Html);
    }

    [Fact]
    public async Task The_row_a_pointer_picks_is_where_the_cursor_is_from_then_on()
    {
        var log = new List<string>();
        var page = Page.Render(Menu(log: log, keepOpen: true));
        await OpenAsync(page);

        await page.On("[role=\"menuitemcheckbox\"]").Click();
        var onThePick = Cursor(page.Html);
        await KeyAsync(page, "ArrowUp");

        Assert.Equal("Show archived", onThePick);
        Assert.Equal("Sort by", Cursor(page.Html));
    }

    [Fact]
    public async Task A_tap_on_a_submenu_row_opens_it_and_a_second_tap_closes_it()
    {
        var page = Page.Render(Menu());
        await OpenAsync(page);
        var row = Regex.Match(page.Html, "data-ui-menu-submenu=\"\"[^>]*><button id=\"([^\"]+)\"").Groups[1].Value;

        await page.On("#" + row).Click();
        var opened = page.Html;
        await page.On("#" + row).Click();

        Assert.Matches("<div[^>]*data-ui-menu-submenu=\"\"[^>]*data-open=\"\"", opened);
        Assert.Equal("Sort by", Cursor(opened));
        Assert.DoesNotMatch("<div[^>]*data-ui-menu-submenu=\"\"[^>]*data-open=\"\"", page.Html);
    }

    [Fact]
    public async Task Closing_forgets_the_cursor_and_shuts_every_submenu()
    {
        var page = Page.Render(Menu());
        await OpenAsync(page);
        await KeyAsync(page, "ArrowDown");
        await KeyAsync(page, "ArrowDown");
        await KeyAsync(page, "ArrowRight");

        await page.On("[popover]").Raise("toggle", "{\"oldState\":\"open\",\"newState\":\"closed\"}");

        Assert.Equal("", Cursor(page.Html));
        Assert.DoesNotContain("data-open", page.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("aria-expanded=\"true\"", page.Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ArrowDown_from_the_trigger_reaches_the_menu_before_it_reports_open_and_still_lands_on_the_first_row()
    {
        var page = Page.Render(Menu());

        // The runtime clicks the trigger and hands the same key to the menu; the popover's toggle comes after.
        await KeyAsync(page, "ArrowDown");
        await OpenAsync(page);

        Assert.Equal("Edit", Cursor(page.Html));
    }

    [Fact]
    public async Task A_controlled_dropdown_tells_the_page_what_the_reader_did_and_shows_what_the_page_says()
    {
        var heard = new List<bool>();
        var page = Page.Render(Ui.Dropdown.Open(false).OnToggle(heard.Add)[
            Button["Actions"],
            Ui.Menu[Ui.MenuItem["Edit"]]
        ]);

        await OpenAsync(page);

        Assert.Equal([true], heard);
        // The page did not change its mind, so the menu still says closed: the runtime will close it again.
        Assert.Contains("data-rask-popover-open=\"false\"", page.Html, StringComparison.Ordinal);
        Assert.Contains("aria-expanded=\"false\"", page.Html, StringComparison.Ordinal);
    }
}
