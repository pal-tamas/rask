using Rask.DevTools.Panel;
using Rask.DevTools.Probe;
using Rask.Testing;

namespace Rask.DevTools.Tests.Panel;

/// <summary>
///     The Tree tab's own tree, drawn from Flux pieces since Flux has none: the WAI-ARIA tree a developer walks with the
///     keyboard, opens, closes and selects from.
/// </summary>
public sealed class DevToolsTreeTests
{
    private const string Tree = "[role=\"tree\"]";

    // App › Card › (Row, Box › Leaf), all open: the tab opens a fresh tree eight levels deep.
    private static Page Render()
    {
        var feed = new DevToolsFeed();
        var leaf = new DevToolsComponentNode(5, "Leaf", null, [], []);
        var box = new DevToolsComponentNode(4, "Box", null, [], [leaf]);
        var row = new DevToolsComponentNode(3, "Row", "r1", [], []);
        var card = new DevToolsComponentNode(2, "Card", null, [], [row, box]);
        feed.RecordTree(new DevToolsComponentNode(1, "App", null, [], [card]));
#pragma warning disable RASK014 // the tab rendered alone, the way the panel page would chain it, with a feed given by hand
        return Page.Render(new DevToolsTreeTab { Feed = feed });
#pragma warning restore RASK014
    }

    private static string? Cursor(Page page) => page.Find(Tree).Attributes["aria-activedescendant"];

    private static Task Press(Page page, string key) => page.On(Tree).Raise("keydown", "{\"key\":\"" + key + "\"}");

    private static string Row(long id) => "rask-devtools-tree-n-" + id;

    [Fact]
    public void The_tree_is_one_focusable_element_whose_rows_say_their_level_and_state()
    {
        var page = Render();

        var tree = page.Find(Tree);
        var card = page.Find("#" + Row(2));
        var leaf = page.Find("#" + Row(5));

        Assert.Equal("0", tree.Attributes["tabindex"]);
        Assert.Equal("Component tree", tree.Attributes["aria-label"]);
        Assert.Equal(Row(1), Cursor(page));
        Assert.Equal("treeitem", card.Attributes["role"]);
        Assert.Equal("2", card.Attributes["aria-level"]);
        Assert.Equal("true", card.Attributes["aria-expanded"]);
        Assert.Equal("false", card.Attributes["aria-selected"]);
        Assert.False(leaf.Attributes.ContainsKey("aria-expanded"));
    }

    [Fact]
    public async Task Arrow_down_moves_the_cursor_and_enter_selects_the_row_it_is_on()
    {
        var page = Render();

        await Press(page, "ArrowDown");
        await Press(page, "ArrowDown");
        await Press(page, "Enter");

        Assert.Equal(Row(3), Cursor(page));
        var selected = Assert.Single(page.FindAll("[aria-selected=\"true\"]"));
        Assert.Equal(Row(3), selected.Attributes["id"]);
    }

    [Fact]
    public async Task Arrow_left_closes_the_open_row_and_arrow_right_opens_it_again()
    {
        var page = Render();
        await Press(page, "ArrowDown");

        await Press(page, "ArrowLeft");
        var closed = page.FindAll("[role=\"treeitem\"]").Count;
        await Press(page, "ArrowRight");

        Assert.Equal(2, closed);
        Assert.Equal("true", page.Find("#" + Row(2)).Attributes["aria-expanded"]);
        Assert.Equal(5, page.FindAll("[role=\"treeitem\"]").Count);
    }

    [Fact]
    public async Task Typing_the_start_of_a_name_moves_the_cursor_to_that_row()
    {
        var page = Render();

        await Press(page, "L");

        Assert.Equal(Row(5), Cursor(page));
    }

    [Fact]
    public async Task Clicking_a_row_selects_it_and_puts_the_cursor_on_it()
    {
        var page = Render();

        await page.On("#" + Row(4) + "-l").Click();

        Assert.Equal(Row(4), Cursor(page));
        Assert.Equal("true", page.Find("#" + Row(4)).Attributes["aria-selected"]);
    }

    [Fact]
    public async Task A_rows_chevron_closes_its_branch()
    {
        var page = Render();

        await page.On("#" + Row(4) + "-l > span[aria-hidden]").Click();

        Assert.Equal("false", page.Find("#" + Row(4)).Attributes["aria-expanded"]);
        Assert.Empty(page.FindAll("#" + Row(5)));
    }
}
