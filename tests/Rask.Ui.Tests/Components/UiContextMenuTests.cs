using System.Text.RegularExpressions;
using Rask.Testing;

namespace Rask.UiTests.Components;

/// <summary>
///     A menu opened by a right-click on its target: <c>Ui.ContextMenu.Target(...)[Ui.MenuItem…]</c>.
/// </summary>
/// <remarks>
///     The opening is the runtime's — it shows the popover at the pointer, which the site's browser suite drives —
///     so what C# owns, and what is pinned here, is the markup that hook looks for and the menu itself, which is the
///     same <see cref="UiMenuSurface" /> a dropdown renders.
/// </remarks>
public partial class UiContextMenuTests : global::Rask.Core.RaskMarkup
{
    private static global::Rask.Core.Component Menu() =>
        Ui.ContextMenu.Target(Div.Class("card")["Invoice 42"])[
            Ui.MenuItem.Text("Open"),
            Ui.MenuItem.Text("Duplicate").Disabled(true),
            Ui.MenuItem.Text("Delete").Tone(Ui.Tone.Error)
        ];

    [Fact]
    public void The_target_is_drawn_and_names_the_popover_the_runtime_opens()
    {
        var html = Menu().ToHtml();

        Assert.Contains("Invoice 42", html, StringComparison.Ordinal);
        var panel = Regex.Match(html, "data-rask-contextmenu=\"([^\"]+)\"").Groups[1].Value;
        Assert.StartsWith("uicm-", panel, StringComparison.Ordinal);
        Assert.Matches("<div id=\"" + Regex.Escape(panel) + "\"[^>]*popover=\"auto\"", html);
    }

    [Fact]
    public void There_is_no_button_because_the_target_is_what_opens_it()
    {
        var html = Menu().ToHtml();

        Assert.DoesNotContain("popovertarget", html, StringComparison.Ordinal);
        Assert.DoesNotContain("aria-haspopup", html, StringComparison.Ordinal);
        // Nothing to be labelled by: an aria-labelledby naming an id nobody wrote points at nothing.
        Assert.DoesNotContain("aria-labelledby", html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_panel_sits_at_the_position_the_runtime_writes()
    {
        var html = Menu().ToHtml();

        Assert.Contains("left:var(--rask-context-x,0px)", html, StringComparison.Ordinal);
        Assert.Contains("top:var(--rask-context-y,0px)", html, StringComparison.Ordinal);
        Assert.DoesNotContain("position-anchor", html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_rows_are_the_menu_rows_a_dropdown_takes()
    {
        var html = Menu().ToHtml();

        Assert.Contains("role=\"menu\"", html, StringComparison.Ordinal);
        Assert.Equal(3, Regex.Matches(html, "role=\"menuitem\"").Count);
    }

    [Fact]
    public async Task Opening_puts_the_cursor_on_the_first_item_and_the_arrows_skip_what_is_disabled()
    {
        var page = Page.Render(Menu());

        await page.On("[popover]").RaiseAsync("toggle", "{\"oldState\":\"closed\",\"newState\":\"open\"}");
        var first = Regex.Match(page.Html, "aria-activedescendant=\"([^\"]+)\"").Groups[1].Value;
        Assert.NotEmpty(first);
        Assert.Contains("data-open", page.Html, StringComparison.Ordinal);

        await page.On("[role=\"menu\"][autofocus]").RaiseAsync("keydown", "{\"key\":\"ArrowDown\"}");
        var second = Regex.Match(page.Html, "aria-activedescendant=\"([^\"]+)\"").Groups[1].Value;

        // "Duplicate" is disabled, so the cursor lands on "Delete" — the third row.
        Assert.Matches("-mi-0$", first);
        Assert.Matches("-mi-2$", second);
    }

    [Fact]
    public void Two_menus_on_one_page_do_not_share_ids()
    {
        var html = Div[Menu(), Menu()].ToHtml();
        var panels = Regex.Matches(html, "data-rask-contextmenu=\"([^\"]+)\"").Select(m => m.Groups[1].Value).ToList();

        Assert.Equal(2, panels.Distinct().Count());
    }
}
