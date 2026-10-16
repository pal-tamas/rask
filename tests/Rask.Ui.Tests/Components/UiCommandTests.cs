using Rask.Core;
using Page = Rask.Testing.Page;

namespace Rask.UiTests.Components;

/// <summary>
///     Flux's <c>flux:command</c>: <c>Ui.Command[Ui.CommandInput, Ui.CommandItems[Ui.CommandItem…]]</c>.
/// </summary>
/// <remarks>
///     The filter, the lit command and Enter are the runtime's (<c>rask-command.ts</c>): typing into a palette and
///     walking it send the page nothing, and are held to fluxui.dev step by step in a browser
///     (<c>scripts/flux/parity-command.mjs</c>, <c>UiCommandBrowserTests</c>). What C# owns is pinned here — the
///     markup those hooks read — and the rules are <c>CommandPaletteTests</c>' in Rask.Core.Tests.
/// </remarks>
public partial class UiCommandTests : RaskMarkup
{
    private string _pressed = "nothing";

    private Component Palette() =>
        Ui.Command[
            Ui.CommandInput.Placeholder("Search..."),
            Ui.CommandItems[
                Ui.CommandItem.Icon(Ui.IconName.UserPlus).Kbd("⌘A").OnClick(() => { _pressed = "assign"; })["Assign to…"],
                Ui.CommandItem.Icon(Ui.IconName.DocumentPlus)["Create new file"],
                Ui.CommandItem["Réglages"]
            ]
        ];

    [Fact]
    public void Each_part_is_marked_as_Flux_marks_it_and_the_root_asks_the_runtime_for_the_palette()
    {
        var page = Page.Render(Palette);

        var root = page.Find("[data-ui-command]");

        Assert.True(root.Attributes.ContainsKey("data-rask-command"));
        Assert.NotNull(page.Find("[data-ui-command] > [data-ui-command-input] > input"));
        Assert.NotNull(page.Find("[data-ui-command] > [data-ui-command-items][role=\"listbox\"]"));
        Assert.Equal(3, page.FindAll("[data-ui-command-items] > [data-ui-command-item][role=\"option\"]").Count);
    }

    [Fact]
    public void The_search_box_holds_no_value_and_no_handler_of_the_page()
    {
        var page = Page.Render(Palette);

        var box = page.Find("[data-ui-command-input] input");

        Assert.Null(box.Attribute("value"));
        Assert.Equal("none", box.Attribute("aria-autocomplete"));
        Assert.Equal("off", box.Attribute("autocomplete"));
        Assert.Equal("Search...", box.Attribute("placeholder"));
        Assert.DoesNotContain("data-rask-on-input", page.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-rask-on-keydown", page.Html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_list_is_a_listbox_with_Flux_s_attributes_and_an_empty_row_hidden_at_its_foot()
    {
        var page = Page.Render(Palette);

        var list = page.Find("[data-ui-command-items]");
        var empty = page.Find("[data-rask-command-empty]");

        Assert.Equal("-1", list.Attribute("tabindex"));
        Assert.Equal("true", list.Attribute("aria-multiselectable"));
        Assert.True(empty.Attributes.ContainsKey("data-hidden"));
        Assert.Equal("No results found", page.TextOf("[data-rask-command-empty]"));
    }

    [Fact]
    public void Every_command_carries_the_words_the_search_matches_and_no_lit_state()
    {
        var page = Page.Render(Palette);

        var commands = page.FindAll("[data-ui-command-item]");

        Assert.Equal(["Assign to…", "Create new file", "Réglages"], commands.Select(command => command.Attribute("data-rask-match")));
        Assert.All(commands, command => Assert.False(command.Attributes.ContainsKey("data-active")));
        Assert.All(commands, command => Assert.False(string.IsNullOrEmpty(command.Id)));
    }

    [Fact]
    public void A_command_shows_its_icon_before_its_words_and_its_shortcut_after_them()
    {
        var html = System.Net.WebUtility.HtmlDecode(Palette().ToHtml());

        var row = html[System.Text.RegularExpressions.Regex.Match(html, "data-ui-command-item[ >]").Index..];

        Assert.Contains("</svg></div>Assign to…<div class=\"ms-auto", row, StringComparison.Ordinal);
        Assert.Contains(">⌘A</span></div></div>", row, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Pressing_a_command_runs_its_handler()
    {
        var page = Page.Render(Palette);

        await page.On("[data-ui-command-item][data-rask-match=\"Assign to…\"]").Click();

        Assert.Equal("assign", _pressed);
    }

    [Fact]
    public void The_box_has_a_clear_button_unless_it_is_closable_and_a_close_button_when_it_is()
    {
        var plain = Page.Render(() => Ui.CommandInput);
        var closable = Page.Render(() => Ui.CommandInput.Closable());
        var both = Page.Render(() => Ui.CommandInput.Closable().Clearable());
        var neither = Page.Render(() => Ui.CommandInput.Clearable(false));

        Assert.NotNull(plain.Find("[aria-label=\"Clear command input\"][data-rask-clear]"));
        Assert.Empty(plain.FindAll("[aria-label=\"Close command modal\"]"));
        Assert.Empty(closable.FindAll("[aria-label=\"Clear command input\"]"));
        Assert.NotNull(closable.Find("[aria-label=\"Close command modal\"]"));
        Assert.Equal(2, both.FindAll("button").Count);
        Assert.Empty(neither.FindAll("button"));
    }

    [Fact]
    public void The_clear_button_names_the_box_it_empties_and_stays_out_of_the_tab_order()
    {
        var page = Page.Render(() => Ui.CommandInput);

        var clear = page.Find("[data-rask-clear]");

        Assert.Equal(page.Find("input").Id, clear.Attribute("data-rask-clear"));
        Assert.Equal("-1", clear.Attribute("tabindex"));
    }

    [Fact]
    public void In_a_modal_the_close_button_closes_that_modal()
    {
        var html = Ui.Modal.Name("search")[Ui.Command[Ui.CommandInput.Closable()]].ToHtml();

        var close = Page.Render(() => Ui.Modal.Name("search")[Ui.Command[Ui.CommandInput.Closable()]])
            .Find("[aria-label=\"Close command modal\"]");

        Assert.Contains("data-rask-command", html, StringComparison.Ordinal);
        Assert.Equal("close", close.Attribute("command"));
        Assert.Equal("search", close.Attribute("commandfor"));
    }

    [Fact]
    public void The_palette_is_capped_at_480px_only_while_it_is_handed_no_class()
    {
        var plain = Page.Render(() => Ui.Command).Find("[data-ui-command]");
        var classed = Page.Render(() => Ui.Command.Class("border-none")).Find("[data-ui-command]");

        Assert.Contains("max-w-120", plain.Attribute("class"), StringComparison.Ordinal);
        Assert.DoesNotContain("max-w-120", classed.Attribute("class"), StringComparison.Ordinal);
    }

    [Fact]
    public void A_modal_trigger_opens_its_modal_from_an_input_drawn_as_a_button()
    {
        var trigger = Ui.ModalTrigger.Name("search").Shortcut("mod+k")[
            Ui.Input.Of<string>().As(Ui.InputAs.Button).Placeholder("Search...").Icon(Ui.IconName.MagnifyingGlass).Kbd("⌘K")
        ];

        var button = Page.Render(() => trigger).Find("button");

        Assert.Equal("show-modal", button.Attribute("command"));
        Assert.Equal("search", button.Attribute("commandfor"));
        Assert.Equal("mod+k", button.Attribute("data-rask-shortcut"));
    }
}
