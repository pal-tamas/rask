using System.Text.RegularExpressions;
using Rask.Core;

namespace Rask.UiTests.Components;

/// <summary>
///     What <c>Ui.Dropdown[trigger, Ui.Popover[…]]</c> writes: Flux UI's <c>flux:popover</c>, the panel a
///     dropdown opens when what it holds is not a menu.
/// </summary>
/// <remarks>
///     The markup contract. How the panel LOOKS, where it lands and what the pointer and the keys do are held
///     to Flux's page by <c>scripts/flux/parity.mjs popover</c> and <c>parity-popover.mjs</c>.
/// </remarks>
public partial class UiPopoverTests : global::Rask.Core.RaskMarkup
{
    private static string Html(Func<UiDropdown, UiDropdown>? shape = null, string? panel = null)
    {
        Component trigger = Button.Type(ButtonType.Button)["Options"];
        Component popover = Ui.Popover.Class(panel)[P["Anything"]];

        return (shape?.Invoke(Ui.Dropdown) ?? Ui.Dropdown)[trigger, popover].ToHtml().AsText();
    }

    private static string Panel(string html) => Regex.Match(html, "<div[^>]*data-ui-popover=\"\"[^>]*>").Value;

    private static string PanelId(string html) => Regex.Match(html, "popovertarget=\"([^\"]+)\"").Groups[1].Value;

    [Fact]
    public void The_trigger_opens_the_panel_with_no_script()
    {
        var html = Html();

        var id = PanelId(html);

        Assert.NotEmpty(id);
        Assert.Contains("id=\"" + id + "\"", Panel(html), StringComparison.Ordinal);
        Assert.Contains("popover=\"auto\"", Panel(html), StringComparison.Ordinal);
    }

    [Fact]
    public void The_trigger_carries_the_aria_Flux_writes_on_it()
    {
        var html = Html();

        var trigger = Regex.Match(html, "<button[^>]*>Options").Value;

        Assert.Contains("aria-haspopup=\"true\"", trigger, StringComparison.Ordinal);
        Assert.Contains("aria-expanded=\"false\"", trigger, StringComparison.Ordinal);
        Assert.Contains("aria-controls=\"" + PanelId(html) + "\"", trigger, StringComparison.Ordinal);
    }

    [Fact]
    public void The_panel_is_not_a_menu_and_takes_no_focus_as_it_opens()
    {
        var html = Html();

        var panel = Panel(html);

        Assert.DoesNotContain("role=", html, StringComparison.Ordinal);
        Assert.DoesNotContain("autofocus", panel, StringComparison.Ordinal);
        Assert.Contains("tabindex=\"-1\"", panel, StringComparison.Ordinal);
    }

    [Fact]
    public void The_panel_follows_its_trigger_so_Tab_walks_into_it()
    {
        var html = Html();

        var trigger = html.IndexOf("<button", StringComparison.Ordinal);
        var panel = html.IndexOf("data-ui-popover", StringComparison.Ordinal);

        Assert.True(trigger >= 0 && panel > trigger, html);
    }

    [Fact]
    public void A_closed_panel_stays_hidden_whatever_layout_the_call_site_gives_it()
    {
        var html = Html(panel: "flex flex-col gap-4");

        var classes = Regex.Match(Panel(html), "class=\"([^\"]*)\"").Groups[1].Value;

        Assert.Contains("[&:not(:popover-open)]:hidden", classes, StringComparison.Ordinal);
        Assert.EndsWith(" flex flex-col gap-4", classes, StringComparison.Ordinal);
    }

    [Fact]
    public void The_panels_own_box_weighs_nothing_so_the_call_sites_classes_win()
    {
        var html = Html(panel: "w-80 p-0");

        var defaults = new[] { "[:where(&)]:min-w-48", "[:where(&)]:p-4", "[:where(&)]:rounded-lg", "[:where(&)]:shadow-xs" };

        Assert.All(defaults, name => Assert.Contains(name, Panel(html), StringComparison.Ordinal));
    }

    [Fact]
    public void The_page_behind_a_pressed_popover_is_locked_and_behind_a_hovered_one_is_not()
    {
        var pressed = Html();

        var hovered = Html(dropdown => dropdown.Hover());

        Assert.Contains("data-rask-lock=\"\"", Panel(pressed), StringComparison.Ordinal);
        Assert.DoesNotContain("data-rask-lock", hovered, StringComparison.Ordinal);
        Assert.Contains("data-rask-hover=\"" + PanelId(hovered) + "\"", hovered, StringComparison.Ordinal);
    }

    [Fact]
    public void The_panel_sits_where_the_dropdown_says()
    {
        var html = Html(dropdown => dropdown.Top.End.Gap(16).Offset(32));

        var anchor = Regex.Match(html, "anchor-name:(--uidd-\\d+)").Groups[1].Value;
        var style = Regex.Match(Panel(html), "style=\"([^\"]*)\"").Groups[1].Value;

        Assert.NotEmpty(anchor);
        Assert.StartsWith("position-anchor:" + anchor + ";", style, StringComparison.Ordinal);
        Assert.Contains("inset-block-end:calc(anchor(start) + 16px)", style, StringComparison.Ordinal);
        Assert.Contains("inset-inline-end:calc(anchor(end) + 32px)", style, StringComparison.Ordinal);
    }

    [Fact]
    public void An_open_popover_asks_the_runtime_to_say_so_on_the_dropdown_the_trigger_and_the_panel()
    {
        var html = Html(dropdown => dropdown.Open(true));

        var marks = Regex.Matches(html, "data-rask-open-mark=\"uidd-\\d+-panel\"");

        Assert.Equal(2, marks.Count);
        Assert.DoesNotContain("data-open", html, StringComparison.Ordinal);
        Assert.Contains("data-rask-open-mark=\"\"", Panel(html), StringComparison.Ordinal);
        Assert.Contains("aria-expanded=\"true\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_state_driven_popover_is_mirrored_by_the_runtime()
    {
        var owned = Html(dropdown => dropdown.Open(false));

        var free = Html();

        Assert.Contains("data-rask-popover-open=\"false\"", Panel(owned), StringComparison.Ordinal);
        Assert.DoesNotContain("data-rask-popover-open", free, StringComparison.Ordinal);
        Assert.DoesNotContain("data-open", free, StringComparison.Ordinal);
    }

    [Fact]
    public void A_kit_button_is_a_trigger_too()
    {
        Component trigger = Ui.Button["Options"];
        Component popover = Ui.Popover[P["Anything"]];

        var html = Ui.Dropdown[trigger, popover].ToHtml().AsText();

        Assert.NotEmpty(PanelId(html));
        Assert.Contains("aria-controls=\"" + PanelId(html) + "\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Two_popovers_on_a_page_never_share_a_panel()
    {
        var first = PanelId(Html());

        var second = PanelId(Html());

        Assert.NotEqual(first, second);
    }
}
