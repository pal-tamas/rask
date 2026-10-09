using System.Text.RegularExpressions;
using Rask.Testing;

namespace Rask.UiTests.Components;

/// <summary>
///     A menu opened by a right-click on an area: <c>Ui.Context[area, Ui.Menu[…]]</c>, Flux UI's
///     <c>flux:context</c>.
/// </summary>
/// <remarks>
///     The opening is the runtime's — it shows the popover at the pointer, which the site's browser suite drives —
///     so what C# owns, and what is pinned here, is the markup that hook looks for, where the menu sits against
///     the pointer, and the menu itself, which is the same <see cref="UiMenu" /> a dropdown opens.
/// </remarks>
public partial class UiContextTests : global::Rask.Core.RaskMarkup
{
    private static global::Rask.Core.Component Menu() =>
        Ui.Context[
            Div.Class("card")["Invoice 42"],
            Ui.Menu[
                Ui.MenuItem["Open"],
                Ui.MenuItem.Disabled(true)["Duplicate"],
                Ui.MenuItem.Danger["Delete"]
            ]
        ];

    [Fact]
    public void The_area_is_drawn_and_names_the_menu_the_runtime_opens()
    {
        var html = Menu().ToHtml().AsText();

        var panel = Regex.Match(html, "data-rask-contextmenu=\"([^\"]+)\"").Groups[1].Value;

        Assert.Contains("Invoice 42", html, StringComparison.Ordinal);
        Assert.Matches("<div[^>]*data-ui-context=\"\"", html);
        Assert.StartsWith("uictx-", panel, StringComparison.Ordinal);
        Assert.Matches("<div id=\"" + Regex.Escape(panel) + "\"[^>]*popover=\"auto\"[^>]*role=\"menu\"", html);
    }

    [Fact]
    public void There_is_no_button_because_the_area_is_what_opens_it()
    {
        var html = Menu().ToHtml().AsText();

        var area = html.Split("popover=", 2)[0];

        Assert.DoesNotContain("popovertarget", html, StringComparison.Ordinal);
        Assert.DoesNotContain("aria-haspopup", area, StringComparison.Ordinal);
        Assert.DoesNotContain("aria-expanded", area, StringComparison.Ordinal);
    }

    [Fact]
    public void The_menu_opens_below_the_pointer_reaching_back_from_it_five_pixels_off()
    {
        var html = Menu().ToHtml().AsText();

        var style = Regex.Match(html, "role=\"menu\"[^>]*style=\"([^\"]+)\"").Groups[1].Value;

        Assert.Contains("left:var(--rask-context-x,0px)", style, StringComparison.Ordinal);
        Assert.Contains("top:var(--rask-context-y,0px)", style, StringComparison.Ordinal);
        Assert.Contains("translate:calc(-100% + 0px) calc(0% + 5px + 0px)", style, StringComparison.Ordinal);
        Assert.DoesNotContain("position-anchor", style, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(Ui.ContextPosition.BottomStart, "translate:calc(0% + 3px) calc(0% + 8px + 4px)")]
    [InlineData(Ui.ContextPosition.BottomCenter, "translate:calc(-50% + 3px) calc(0% + 8px + 4px)")]
    [InlineData(Ui.ContextPosition.TopEnd, "translate:calc(-100% + 3px) calc(-100% - 8px + 4px)")]
    [InlineData(Ui.ContextPosition.TopStart, "translate:calc(0% + 3px) calc(-100% - 8px + 4px)")]
    public void Position_Gap_and_Offset_say_which_corner_lands_where(Ui.ContextPosition position, string expected)
    {
        var context = Ui.Context.Position(position).Gap(8).Offset((3, 4))[Div["area"], Ui.Menu[Ui.MenuItem["Open"]]];

        var html = context.ToHtml().AsText();

        Assert.Contains(expected, html, StringComparison.Ordinal);
    }

    [Fact]
    public void Disabled_leaves_the_browsers_own_menu_alone()
    {
        var html = Ui.Context.Disabled(true)[Div["area"], Ui.Menu[Ui.MenuItem["Open"]]].ToHtml().AsText();

        var hook = Regex.IsMatch(html, "data-rask-contextmenu");

        Assert.False(hook);
        Assert.Contains("data-ui-context", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Target_names_a_menu_written_somewhere_else_and_Detail_marks_the_menu_inside()
    {
        var html = Div[
            Ui.Context.Target("row-menu")[Div["area"]],
            Ui.Context.Detail("invoice")[Div["other"], Ui.Menu[Ui.MenuItem["Open"]]],
            Ui.Menu.Id("row-menu")[Ui.MenuItem["Rename"]]
        ].ToHtml().AsText();

        var outside = Regex.Match(html, "<div id=\"row-menu\"[^>]*>").Value;

        Assert.Contains("data-rask-contextmenu=\"row-menu\"", html, StringComparison.Ordinal);
        Assert.Contains("popover=\"auto\"", outside, StringComparison.Ordinal);
        // On its own it still sits where the pointer was.
        Assert.Contains("left:var(--rask-context-x,0px)", outside, StringComparison.Ordinal);
        Assert.Matches("<div[^>]*data-detail=\"invoice\"[^>]*role=\"menu\"", html);
    }

    [Fact]
    public void The_rows_are_the_menu_rows_a_dropdown_takes()
    {
        var html = Menu().ToHtml().AsText();

        var rows = Regex.Matches(html, "role=\"menuitem\"");

        Assert.Equal(3, rows.Count);
        Assert.Matches("<button[^>]*disabled[^>]*>(?:(?!</button>).)*Duplicate", html);
        Assert.Contains("data-active:text-red-600", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_area_says_it_is_open_while_the_menu_is_up_and_its_menu_is_the_one_a_dropdown_takes()
    {
        var page = Page.Render(Menu());

        await page.On("[popover]").Raise("toggle", "{\"oldState\":\"closed\",\"newState\":\"open\"}");

        Assert.Matches("<div[^>]*data-ui-context=\"\"[^>]*data-open=\"\"", page.Html);
        // The same menu, so the same cursor: the runtime's, in the browser.
        Assert.Matches("<div[^>]*role=\"menu\"[^>]* data-rask-menu-cursor=\"\"|<div[^>]* data-rask-menu-cursor=\"\"[^>]*role=\"menu\"", page.Html);
    }

    [Fact]
    public async Task A_page_that_owns_Open_hears_the_reader_and_decides()
    {
        var heard = new List<bool>();
        var page = Page.Render(Ui.Context.Open(false).OnToggle(heard.Add)[Div["area"], Ui.Menu[Ui.MenuItem["Open"]]]);

        await page.On("[popover]").Raise("toggle", "{\"oldState\":\"closed\",\"newState\":\"open\"}");

        Assert.Equal([true], heard);
        Assert.Contains("data-rask-popover-open=\"false\"", page.Html, StringComparison.Ordinal);
    }
}
