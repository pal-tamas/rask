using System.Text.RegularExpressions;
using Rask.Testing;

namespace Rask.UiTests.Components;

/// <summary>
///     The command palette: <c>Ui.Command.Label(...).Shortcut("mod+k")[Ui.MenuItem…]</c>.
/// </summary>
/// <remarks>
///     The dialog opening, the shortcut and Enter pressing a command are the platform's and the runtime's, and so
///     are the filter and the highlight (<c>rask-command.ts</c>): typing into a palette and walking it send the
///     page nothing. What C# owns is pinned here — the markup those hooks read. The rules are
///     <c>CommandPaletteTests</c>' in Rask.Core.Tests, the browser is <c>UiCommandBrowserTests</c>' in
///     Rask.Server.E2E.Tests.
/// </remarks>
public partial class UiCommandTests : global::Rask.Core.RaskMarkup
{
    private static global::Rask.Core.Component Palette() =>
        Ui.Command.Label("Search commands").Shortcut("mod+k")[
            Ui.MenuItem.Icon(Ui.IconName.Plus)["New invoice"],
            Ui.MenuItem.Disabled(true)["Archive"],
            Ui.MenuSeparator,
            Ui.MenuItem["Réglages"],
            Ui.MenuItem.Danger["Sign out"]
        ];

    [Fact]
    public void The_field_opens_a_modal_dialog_and_carries_the_shortcut()
    {
        var html = Palette().ToHtml();

        var dialog = Regex.Match(html, "<dialog id=\"([^\"]+)\"").Groups[1].Value;
        Assert.StartsWith("uicmd-", dialog, StringComparison.Ordinal);
        Assert.Contains("command=\"show-modal\" commandfor=\"" + dialog + "\"", html, StringComparison.Ordinal);
        Assert.Contains("data-rask-shortcut=\"mod+k\"", System.Net.WebUtility.HtmlDecode(html), StringComparison.Ordinal);
        Assert.Contains("data-rask-close-on-pick", html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_shortcut_is_shown_in_both_platforms_words()
    {
        var html = System.Net.WebUtility.HtmlDecode(Palette().ToHtml());

        Assert.Contains(">⌘K</kbd>", html, StringComparison.Ordinal);
        Assert.Contains(">Ctrl K</kbd>", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("mod+shift+p", true, "⌘⇧P")]
    [InlineData("mod+shift+p", false, "Ctrl Shift P")]
    [InlineData("alt+enter", false, "Alt Enter")]
    public void A_shortcut_is_described_the_way_each_platform_writes_it(string shortcut, bool mac, string expected) =>
        Assert.Equal(expected, UiShortcut.Describe(shortcut, mac));

    [Fact]
    public void The_search_box_is_a_combobox_over_a_listbox_of_options()
    {
        var html = Palette().ToHtml();

        Assert.Contains("role=\"combobox\"", html, StringComparison.Ordinal);
        Assert.Contains("aria-expanded=\"true\"", html, StringComparison.Ordinal);
        Assert.Contains("data-rask-press-active", html, StringComparison.Ordinal);
        Assert.Contains("role=\"listbox\"", html, StringComparison.Ordinal);
        Assert.Equal(4, Regex.Matches(html, "role=\"option\"").Count);
        Assert.DoesNotContain("role=\"menuitem\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_palette_asks_the_runtime_for_its_filter_and_highlight_and_gives_the_page_no_handler_for_them()
    {
        var page = Page.Render(Palette());

        var box = page.Find("[role=\"combobox\"]");

        Assert.True(page.Find("dialog").Attributes.ContainsKey("data-rask-command"));
        Assert.Null(box.Attribute("value"));
        Assert.DoesNotContain("data-rask-on-input", page.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-rask-on-keydown", page.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-rask-on-toggle", page.Html, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_command_is_rendered_with_the_words_the_filter_matches_and_a_separator_between_runs()
    {
        var page = Page.Render(Palette());

        var matched = page.FindAll("[role=\"option\"]").Select(option => option.Attribute("data-rask-match"));

        Assert.Equal(["New invoice", "Archive", "Réglages", "Sign out"], matched);
        Assert.Single(page.FindAll("[role=\"separator\"]"));
        Assert.Single(page.FindAll(".ui-command-empty"));
    }

    [Fact]
    public void The_highlight_starts_on_the_first_command_that_can_be_picked()
    {
        var palette = Ui.Command.Label("Search")[Ui.MenuItem.Disabled(true)["Archive"], Ui.MenuItem["Rename"], Ui.MenuItem["Delete"]];

        var options = Page.Render(palette).FindAll("[role=\"option\"]");

        Assert.Equal(["false", "true", "false"], options.Select(option => option.Attribute("aria-selected")));
        Assert.Equal("true", options[0].Attribute("aria-disabled"));
    }

    [Fact]
    public void A_menu_item_outside_a_palette_is_unchanged()
    {
        var html = Ui.Dropdown[Button["Actions"], Ui.Menu[Ui.MenuItem["Archive"]]].ToHtml();

        Assert.Contains("role=\"menuitem\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("aria-selected", html, StringComparison.Ordinal);
    }
}
