using System.Net;
using Rask.DevTools.Panel;
using Rask.DevTools.Probe;
using Rask.Testing;

namespace Rask.DevTools.Tests.Panel;

/// <summary>
///     What the Tree tab hands the panel's script for the page overlays: where each row's node is, the anchors a pick
///     matches against, and what a pick reported back does to the tree.
/// </summary>
/// <remarks>
///     Rendered in-process: the page side of hover and pick (drawing, matching) is the host script's, tested in Node, and
///     the whole round trip through a browser is the devtools' hand-run check.
/// </remarks>
public sealed class DevToolsTreeTabPickTests
{
    // root › Card › Row, and a chain ten levels deep under Card, deeper than the tab opens on its own.
    private static DevToolsComponentNode Tree()
    {
        var deep = new DevToolsComponentNode(99, "Deepest", null, [], [], At: "0.0.0.0.0.0.0.0.0|0|1");
        for (var level = 9; level >= 1; level--)
        {
            deep = new DevToolsComponentNode(20 + level, "Level" + level, null, [], [deep], At: null);
        }

        var row = new DevToolsComponentNode(3, "Row", "r1", [], [], At: "0|0|1");
        var card = new DevToolsComponentNode(2, "Card", null, [], [row, deep], At: "|0|1");
        return new DevToolsComponentNode(1, "App", null, [], [card]);
    }

    private static RenderedComponent Render()
    {
        var feed = new DevToolsFeed();
        feed.RecordTree(Tree());
#pragma warning disable RASK014 // the tab rendered alone, the way the panel page would chain it, with a feed given by hand
        return Test.Render(new DevToolsTreeTab { Feed = feed });
#pragma warning restore RASK014
    }

    private static string? AnchorsOf(RenderedComponent page) =>
        page.FindAll("[data-rask-devtools-anchors]").Select(n => n.Attributes["data-rask-devtools-anchors"]).SingleOrDefault()
            is { } raw ? WebUtility.HtmlDecode(raw) : null;

    [Fact]
    public void A_row_carries_its_nodes_place_and_label_and_a_node_without_one_carries_neither()
    {
        var page = Render();

        var places = page.FindAll("[data-rask-devtools-at]");
        Assert.Contains(places, n => n.Attributes["data-rask-devtools-at"] == "0|0|1"
                                     && n.Attributes["data-rask-devtools-label"] == "Row");
        // Level1 has no place (it rendered nothing of its own); nothing on its row points anywhere.
        Assert.DoesNotContain(places, n => n.Attributes["data-rask-devtools-label"] == "Level1");
    }

    [Fact]
    public async Task Pick_publishes_the_anchors_and_pressing_it_again_takes_them_back()
    {
        var page = Render();
        Assert.Null(AnchorsOf(page));

        await page.On("[aria-pressed=\"false\"]").ClickAsync();

        var anchors = AnchorsOf(page);
        Assert.NotNull(anchors);
        // Tree order, parents first, only nodes with a place — the order the host's tie-break relies on.
        Assert.Equal("[[\"2\",\"|0|1\",\"Card\"],[\"3\",\"0|0|1\",\"Row\"],[\"99\",\"0.0.0.0.0.0.0.0.0|0|1\",\"Deepest\"]]", anchors);
        Assert.Single(page.FindAll("[aria-pressed=\"true\"]"));

        await page.On("[aria-pressed=\"true\"]").ClickAsync();

        Assert.Null(AnchorsOf(page));
    }

    [Fact]
    public async Task A_pick_opens_the_way_to_the_node_selects_it_and_ends_the_pick()
    {
        var page = Render();
        await page.On("[aria-pressed=\"false\"]").ClickAsync();
        // Too deep to be open on its own: no row says it (the anchors do, which is what a pick matches against).
        Assert.DoesNotContain(page.FindAll("[role=\"treeitem\"]"), n => n.TextContent.Contains("Deepest", StringComparison.Ordinal));

        await page.On("[data-rask-devtools-picked]").RaiseAsync("keydown", "{\"key\":\"pick:99\"}");

        var selected = Assert.Single(page.FindAll("[aria-selected=\"true\"]"));
        Assert.Contains("Deepest", selected.TextContent, StringComparison.Ordinal);
        Assert.Null(AnchorsOf(page));
        Assert.Empty(page.FindAll("[aria-pressed=\"true\"]"));
    }

    // The Errors tab's "Show in tree": the tab opens with the component asked for already opened to and selected.
    [Fact]
    public void A_component_to_reveal_is_opened_to_and_selected_when_the_tab_renders()
    {
        var feed = new DevToolsFeed();
        feed.RecordTree(Tree());
#pragma warning disable RASK014 // the tab rendered alone, the way the panel page would chain it, with a feed given by hand
        var page = Test.Render(new DevToolsTreeTab { Feed = feed, Reveal = 99 });
#pragma warning restore RASK014

        var selected = Assert.Single(page.FindAll("[aria-selected=\"true\"]"));
        Assert.Contains("Deepest", selected.TextContent, StringComparison.Ordinal);
    }

    // An element exists only in the view with tags shown, and the tags are switched on after the tab first rendered: the
    // pick has to be resolved in the view on screen when it arrives.
    [Fact]
    public async Task An_element_picked_after_the_tags_were_shown_is_selected()
    {
        var feed = new DevToolsFeed();
        var button = new DevToolsComponentNode((2L << 20) | 1, "button", null, [], [], IsTag: true, At: "0|1|1");
        var row = new DevToolsComponentNode(3, "Row", "r1", [], [], At: "0|0|1");
        feed.RecordTree(new DevToolsComponentNode(1, "App", null, [],
            [new DevToolsComponentNode(2, "Card", null, [], [row, button], At: "|0|1")]));
#pragma warning disable RASK014 // the tab rendered alone, the way the panel page would chain it, with a feed given by hand
        var page = Test.Render(new DevToolsTreeTab { Feed = feed });
#pragma warning restore RASK014

        await page.On("input[type=\"checkbox\"]").ChangeAsync("true");
        await page.On("[aria-pressed=\"false\"]").ClickAsync();
        await page.On("[data-rask-devtools-picked]").RaiseAsync("keydown", "{\"key\":\"pick:" + button.Id + "\"}");

        var selected = Assert.Single(page.FindAll("[aria-selected=\"true\"]"));
        Assert.Contains("<button>", selected.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_cancelled_pick_ends_the_pick_and_selects_nothing()
    {
        var page = Render();
        await page.On("[aria-pressed=\"false\"]").ClickAsync();

        await page.On("[data-rask-devtools-picked]").RaiseAsync("keydown", "{\"key\":\"pick:cancel\"}");

        Assert.Null(AnchorsOf(page));
        Assert.Empty(page.FindAll("[aria-selected=\"true\"]"));
    }

    [Fact]
    public void A_path_to_a_node_names_every_ancestor_and_none_for_a_missing_node()
    {
        var ancestors = new List<long>();

        Assert.True(DevToolsTreeTab.PathTo(Tree(), 99, ancestors));

        Assert.Equal([1L, 2L, 21L, 22L, 23L, 24L, 25L, 26L, 27L, 28L, 29L], ancestors);

        ancestors.Clear();
        Assert.False(DevToolsTreeTab.PathTo(Tree(), 12345, ancestors));

        Assert.Empty(ancestors);
    }
}
