using System.Text.RegularExpressions;
using Rask.Testing;

namespace Rask.UiTests.Components;

/// <summary>
///     What the PAGE holds of an open menu, driven through the real handlers: that it is open, and what its
///     checkable rows write.
/// </summary>
/// <remarks>
///     Where the reader is in the menu — the lit row, the focused one, the open flyouts, every key and every
///     move of the pointer — is not the page's state and sends it nothing: the runtime keeps it in the browser
///     (<c>data-rask-menu-cursor</c>). That is held in a real browser, against what Flux UI's live dropdown
///     did, by <c>UiMenuHookTests</c> on a live Server page and by the site's suite in WebAssembly.
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

    private static Task OpenAsync(Page page) =>
        page.On("[popover]").Raise("toggle", "{\"oldState\":\"closed\",\"newState\":\"open\"}");

    [Fact]
    public async Task Opening_writes_no_cursor_and_the_trigger_says_it_is_open()
    {
        var page = Page.Render(Menu());

        await OpenAsync(page);

        // No row is lit and none is a tab stop in anything a render writes: both are the runtime's.
        Assert.DoesNotContain("data-active=", page.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("tabindex=\"0\"", page.Html, StringComparison.Ordinal);
        Assert.Contains("aria-expanded=\"true\"", page.Html, StringComparison.Ordinal);
        Assert.Matches("<div[^>]*data-ui-dropdown=\"\"[^>]*data-open=\"\"", page.Html);
        Assert.Matches("<button[^>]*data-open=\"\"[^>]*popovertarget", page.Html);
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
