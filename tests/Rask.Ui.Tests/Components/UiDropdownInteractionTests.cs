using System.Text.RegularExpressions;
using Rask.Testing;

namespace Rask.UiTests.Components;

/// <summary>
///     The dropdown's keyboard cursor, its submenus and its checkable items, driven through the real handlers.
/// </summary>
/// <remarks>
///     <c>RaskTest</c> dispatches in-process and re-renders, so none of this needs a browser. What does — the popover
///     actually opening, Enter pressing the row, the safe triangle — is <c>UiKitActionsTests</c>'.
/// </remarks>
public partial class UiDropdownInteractionTests : global::Rask.Core.RaskMarkup
{
    private sealed class View
    {
        public bool ShowArchived { get; set; }

        public string Sort { get; set; } = "name";
    }

    private static global::Rask.Core.Component Menu(View? view = null, List<string>? log = null)
    {
        view ??= new View();
        return Ui.Dropdown.Trigger("Actions")[
            Ui.MenuItem.Key("edit").Text("Edit").OnClick(() => log?.Add("edit")),
            Ui.MenuItem.Key("dup").Text("Duplicate").Disabled(true),
            Ui.MenuSub.Key("sort").Heading("Sort by")[
                Ui.MenuRadioGroup.Bind(() => view.Sort).Key("sort-group").Options([("name", "Name"), ("date", "Date")])
            ],
            Ui.MenuSeparator.Key("sep"),
            Ui.MenuCheckbox.Bind(() => view.ShowArchived).Key("archived").Text("Show archived"),
            Ui.MenuItem.Key("delete").Text("Delete").Tone(Ui.Tone.Error)
        ];
    }

    // The row the menu's aria-activedescendant names, read back as its words — and the row carrying
    // data-highlighted, which must be the same one.
    private static string Cursor(string html)
    {
        var active = Regex.Match(html, "role=\"menu\"[^>]*aria-activedescendant=\"([^\"]+)\"").Groups[1].Value;
        if (active.Length == 0)
        {
            return "";
        }

        var row = Regex.Match(html, "<button id=\"" + Regex.Escape(active) + "\"([^>]*)>(.*?)</button>", RegexOptions.Singleline);
        Assert.Contains("data-highlighted", row.Groups[1].Value, StringComparison.Ordinal);
        return Regex.Replace(Regex.Replace(row.Groups[2].Value, "<kbd.*?</kbd>", ""), "<[^>]+>", "").Trim();
    }

    private static Task OpenAsync(RenderedComponent page) =>
        page.On("[popover]").RaiseAsync("toggle", "{\"oldState\":\"closed\",\"newState\":\"open\"}");

    private static Task KeyAsync(RenderedComponent page, string key) =>
        page.On("[role=\"menu\"][autofocus]").RaiseAsync("keydown", $"{{\"key\":\"{key}\"}}");

    [Fact]
    public async Task Opening_puts_the_cursor_on_the_first_item_and_the_trigger_says_it_is_open()
    {
        var page = RaskTest.Render(Menu());
        Assert.Equal("", Cursor(page.Html));

        await OpenAsync(page);

        Assert.Equal("Edit", Cursor(page.Html));
        Assert.Contains("aria-expanded=\"true\"", page.Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_arrows_skip_what_is_disabled_and_wrap_at_the_ends()
    {
        var page = RaskTest.Render(Menu());
        await OpenAsync(page);

        await KeyAsync(page, "ArrowDown");
        // "Duplicate" is disabled: the cursor steps over it.
        Assert.Equal("Sort by", Cursor(page.Html));

        await KeyAsync(page, "End");
        Assert.Equal("Delete", Cursor(page.Html));

        await KeyAsync(page, "ArrowDown");
        // A menu wraps, unlike a tree.
        Assert.Equal("Edit", Cursor(page.Html));

        await KeyAsync(page, "ArrowUp");
        Assert.Equal("Delete", Cursor(page.Html));

        await KeyAsync(page, "Home");
        Assert.Equal("Edit", Cursor(page.Html));
    }

    [Fact]
    public async Task Right_opens_a_submenu_and_walks_into_it_and_left_comes_back()
    {
        var page = RaskTest.Render(Menu());
        await OpenAsync(page);
        await KeyAsync(page, "ArrowDown");
        Assert.Equal("Sort by", Cursor(page.Html));

        await KeyAsync(page, "ArrowRight");
        Assert.Contains("class=\"ui-menu-sub\" data-open", page.Html, StringComparison.Ordinal);
        Assert.Equal("Name", Cursor(page.Html));

        // Inside the submenu the arrows move among its own rows.
        await KeyAsync(page, "ArrowDown");
        Assert.Equal("Date", Cursor(page.Html));
        await KeyAsync(page, "ArrowDown");
        Assert.Equal("Name", Cursor(page.Html));

        await KeyAsync(page, "ArrowLeft");
        Assert.DoesNotContain("data-open", page.Html.Split("role=\"menu\"", 2)[1].Split("ui-menu-flyout")[0], StringComparison.Ordinal);
        Assert.Equal("Sort by", Cursor(page.Html));
    }

    [Fact]
    public async Task Typing_a_letter_jumps_to_the_next_item_starting_with_it()
    {
        var page = RaskTest.Render(Menu());
        await OpenAsync(page);

        await KeyAsync(page, "d");
        // Never onto a disabled row: "Duplicate" is passed over for "Delete".
        Assert.Equal("Delete", Cursor(page.Html));

        // A fresh press on another menu: letters typed together build a prefix, so this is its own open.
        var other = RaskTest.Render(Menu());
        await OpenAsync(other);
        await KeyAsync(other, "s");
        Assert.Equal("Sort by", Cursor(other.Html));
    }

    [Fact]
    public async Task A_modified_key_is_left_to_the_browser()
    {
        var page = RaskTest.Render(Menu());
        await OpenAsync(page);

        await page.On("[role=\"menu\"][autofocus]").RaiseAsync("keydown", "{\"key\":\"ArrowDown\",\"ctrlKey\":true}");

        Assert.Equal("Edit", Cursor(page.Html));
    }

    [Fact]
    public async Task Checkable_items_write_back_to_the_model()
    {
        var view = new View();
        var page = RaskTest.Render(Menu(view));
        await OpenAsync(page);

        await page.On("[role=\"menuitemcheckbox\"]").ClickAsync();
        Assert.True(view.ShowArchived);
        Assert.Contains("aria-checked=\"true\"", page.Html, StringComparison.Ordinal);

        var date = Regex.Match(page.Html, "<button id=\"([^\"]+)\"[^>]*role=\"menuitemradio\"[^>]*>(?:(?!</button>).)*Date", RegexOptions.Singleline)
            .Groups[1].Value;
        await page.On("#" + date).ClickAsync();
        Assert.Equal("date", view.Sort);
    }

    private sealed partial class ControlledHost : global::Rask.Core.Component
    {
        private bool _on;
        private string _sort = "name";

        protected override global::Rask.Core.Component? Render() =>
            Ui.Dropdown.Key("dd").Trigger("View")[
                Ui.MenuCheckbox.Key("on").Value(_on).Text("Show archived").OnChange(v => { _on = v; }),
                Ui.MenuRadioGroup.Value(_sort).Options([("name", "Name"), ("date", "Date")])
                    .OnChange(v => { _sort = v; })
            ];
    }

    // The same host with every step written BEFORE Key, and the generic radio group keyed too (#1118). Steps ahead
    // of Key used to land on the instance the key then discarded, so the checkbox kept the value from the render
    // its key was first claimed on; and `Ui.MenuRadioGroup.Key(…)` did not compile (CS0315) at all.
    private sealed partial class KeyLastHost : global::Rask.Core.Component
    {
        private bool _on;
        private string _sort = "name";

        protected override global::Rask.Core.Component? Render() =>
            Ui.Dropdown.Trigger("View").Key("dd")[
                Ui.MenuCheckbox.Value(_on).Text("Show archived").OnChange(v => { _on = v; }).Key("on"),
                Ui.MenuRadioGroup.Key("sort").Value(_sort).Options([("name", "Name"), ("date", "Date")])
                    .OnChange(v => { _sort = v; })
            ];
    }

    [Fact]
    public async Task Steps_written_before_Key_still_reach_the_item_the_key_keeps()
    {
        var page = RaskTest.Render(new KeyLastHost());
        await OpenAsync(page);

        await page.On("[role=\"menuitemcheckbox\"]").ClickAsync();
        Assert.Contains("aria-checked=\"true\"", Regex.Match(page.Html, "<button[^>]*role=\"menuitemcheckbox\"[^>]*>").Value, StringComparison.Ordinal);

        // And back: the second redraw is the one a stale instance got wrong, holding the first claim's value.
        await page.On("[role=\"menuitemcheckbox\"]").ClickAsync();
        Assert.Contains("aria-checked=\"false\"", Regex.Match(page.Html, "<button[^>]*role=\"menuitemcheckbox\"[^>]*>").Value, StringComparison.Ordinal);

        var date = Regex.Match(page.Html, "<button id=\"([^\"]+)\"[^>]*role=\"menuitemradio\"[^>]*>(?:(?!</button>).)*Date", RegexOptions.Singleline)
            .Groups[1].Value;
        await page.On("#" + date).ClickAsync();
        Assert.Contains(
            "aria-checked=\"true\"",
            Regex.Match(page.Html, "<button id=\"" + Regex.Escape(date) + "\"[^>]*>").Value,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Controlled_checkable_items_redraw_from_the_parents_state()
    {
        // Value + OnChange: the PARENT owns the state, re-renders, and hands the dropdown new items. The redraw is
        // the proof the new value reached the rows inside the popover.
        var page = RaskTest.Render(new ControlledHost());
        await OpenAsync(page);

        await page.On("[role=\"menuitemcheckbox\"]").ClickAsync();
        Assert.Contains("aria-checked=\"true\"", Regex.Match(page.Html, "<button[^>]*role=\"menuitemcheckbox\"[^>]*>").Value, StringComparison.Ordinal);

        var date = Regex.Match(page.Html, "<button id=\"([^\"]+)\"[^>]*role=\"menuitemradio\"[^>]*>(?:(?!</button>).)*Date", RegexOptions.Singleline)
            .Groups[1].Value;
        await page.On("#" + date).ClickAsync();
        Assert.Contains(
            "aria-checked=\"true\"",
            Regex.Match(page.Html, "<button id=\"" + Regex.Escape(date) + "\"[^>]*>").Value,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Closing_resets_the_cursor_and_the_open_submenus()
    {
        var page = RaskTest.Render(Menu());
        await OpenAsync(page);
        await KeyAsync(page, "ArrowDown");
        await KeyAsync(page, "ArrowRight");

        await page.On("[popover]").RaiseAsync("toggle", "{\"oldState\":\"open\",\"newState\":\"closed\"}");

        Assert.Equal("", Cursor(page.Html));
        Assert.DoesNotContain("data-open", page.Html, StringComparison.Ordinal);
        Assert.Contains("aria-expanded=\"false\"", page.Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_tap_on_a_submenu_row_opens_it()
    {
        // No hover on a touch screen: the row's own click is the way in.
        var page = RaskTest.Render(Menu());
        await OpenAsync(page);

        await page.On("[aria-haspopup=\"menu\"][role=\"menuitem\"]").ClickAsync();

        Assert.Contains("class=\"ui-menu-sub\" data-open", page.Html, StringComparison.Ordinal);
        Assert.Equal("Name", Cursor(page.Html));
    }

    [Fact]
    public async Task A_controlled_dropdown_hears_only_the_changes_it_did_not_make()
    {
        var heard = new List<bool>();
        var page = RaskTest.Render(Ui.Dropdown.Trigger("Actions").Open(false).OnToggle(open => heard.Add(open))[
            Ui.MenuItem.Key("a").Text("A")
        ]);

        // The reader opened it: the page is told.
        await OpenAsync(page);
        Assert.Equal([true], heard);
    }

    [Fact]
    public async Task The_registry_survives_a_second_render()
    {
        // Items register as they render; a cached item would drop out of the cursor's list.
        var log = new List<string>();
        var page = RaskTest.Render(Menu(log: log));
        await OpenAsync(page);
        page.Render();
        page.Render();

        await KeyAsync(page, "End");
        Assert.Equal("Delete", Cursor(page.Html));
        await KeyAsync(page, "Home");
        Assert.Equal("Edit", Cursor(page.Html));
    }
}
