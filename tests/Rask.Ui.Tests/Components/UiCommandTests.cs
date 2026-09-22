using System.Text.RegularExpressions;
using Rask.Testing;

namespace Rask.Ui.Tests.Components;

/// <summary>
///     The command palette: <c>UiCommand.Label(...).Shortcut("mod+k")[UiMenuItem…]</c>.
/// </summary>
/// <remarks>
///     The dialog opening, the shortcut and Enter pressing a command are the platform's and the runtime's, and the
///     site's browser suite drives them. What C# owns is pinned here: the markup those hooks look for, the filter,
///     and the cursor.
/// </remarks>
public partial class UiCommandTests : global::Rask.Core.RaskMarkup
{
    private static global::Rask.Core.Component Palette() =>
        UiCommand.Label("Search commands").Shortcut("mod+k")[
            UiMenuItem.Text("New invoice").Icon(UiIconName.Plus),
            UiMenuItem.Text("Archive").Disabled(true),
            UiMenuSeparator,
            UiMenuItem.Text("Réglages"),
            UiMenuItem.Text("Sign out").Tone(UiTone.Error)
        ];

    private static Task TypeAsync(Page page, string text) =>
        page.On("[role=\"combobox\"]").InputAsync(text);

    private static Task KeyAsync(Page page, string key) =>
        page.On("[role=\"combobox\"]").RaiseAsync("keydown", $"{{\"key\":\"{key}\"}}");

    private static string Highlighted(string html)
    {
        var id = Regex.Match(html, "role=\"combobox\"[^>]*aria-activedescendant=\"([^\"]+)\"").Groups[1].Value;
        var row = Regex.Match(html, "id=\"" + Regex.Escape(id) + "\"[^>]*>(.*?)</(button|a)>", RegexOptions.Singleline);
        return Regex.Replace(row.Groups[1].Value, "<[^>]+>", "").Trim();
    }

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
    public async Task Typing_narrows_the_list_accent_insensitively_and_drops_the_separators()
    {
        var page = Test.Render(Palette());
        Assert.Contains("ui-menu-separator", page.Html, StringComparison.Ordinal);

        await TypeAsync(page, "reglages");

        Assert.Single(Regex.Matches(page.Html, "role=\"option\""));
        Assert.Contains("glages", page.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("ui-menu-separator", page.Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Nothing_matching_leaves_no_option_for_the_empty_message_to_follow()
    {
        var page = Test.Render(Palette());

        await TypeAsync(page, "zzz");

        Assert.Empty(Regex.Matches(page.Html, "role=\"option\""));
        Assert.Contains("ui-command-empty", page.Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_arrows_move_the_highlight_skip_what_is_disabled_and_wrap()
    {
        var page = Test.Render(Palette());
        // A second render, so the cursor is worked out against a list that has registered.
        await TypeAsync(page, "");
        Assert.Equal("New invoice", Highlighted(page.Html));

        await KeyAsync(page, "ArrowDown");
        // "Archive" is disabled: the highlight steps over it.
        Assert.Equal("Réglages", System.Net.WebUtility.HtmlDecode(Highlighted(page.Html)));

        await KeyAsync(page, "ArrowDown");
        Assert.Equal("Sign out", Highlighted(page.Html));

        await KeyAsync(page, "ArrowDown");
        Assert.Equal("New invoice", Highlighted(page.Html));

        await KeyAsync(page, "ArrowUp");
        Assert.Equal("Sign out", Highlighted(page.Html));
    }

    [Fact]
    public async Task The_highlighted_option_says_so_to_a_screen_reader()
    {
        var page = Test.Render(Palette());
        await TypeAsync(page, "");

        Assert.Single(Regex.Matches(page.Html, "aria-selected=\"true\""));
    }

    [Fact]
    public void A_menu_item_outside_a_palette_is_unchanged()
    {
        var html = UiDropdown.Trigger("Actions")[UiMenuItem.Text("Archive")].ToHtml();

        Assert.Contains("role=\"menuitem\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("aria-selected", html, StringComparison.Ordinal);
    }
}
