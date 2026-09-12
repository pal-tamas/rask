using System.Text.RegularExpressions;
using Rask.Testing;

namespace Rask.Ui.Tests.Components;

/// <summary>
///     The tree's keyboard, its clicks and its two axes, driven through the real handlers.
/// </summary>
/// <remarks>
///     The rendering tests next door see only the first frame, and everything here — the cursor, expanding, selecting,
///     type-ahead — exists only after an event. <c>RaskTest</c> dispatches in-process and re-renders, so none of it
///     needs a browser.
/// </remarks>
public partial class UiTreeInteractionTests : global::Rask.Core.RaskMarkup
{
    private sealed record Node(string Id, string Name, Node[]? Kids = null);

    private static readonly Node[] Files =
    [
        new("src", "src", [new("src/app.cs", "app.cs"), new("src/boot.cs", "boot.cs")]),
        new("docs", "docs", [new("docs/tree.md", "tree.md")]),
        new("README.md", "README.md"),
    ];

    private static UiTree<Node, string> Tree() =>
        (UiTree<Node, string>)UiTree.Roots(Files)
            .NodeKey(n => n.Id)
            .Item(n => Span[n.Name])
            .Label("Files")[n => n.Kids];

    // What the tree says the cursor is on: the row its aria-activedescendant names, read back as the text on screen.
    private static string Cursor(string html)
    {
        var active = Regex.Match(html, "aria-activedescendant=\"([^\"]+)\"").Groups[1].Value;
        if (active.Length == 0)
        {
            return "";
        }

        var row = Regex.Match(html, "id=\"" + Regex.Escape(active) + "-l\"[^>]*>(.*?)</div>", RegexOptions.Singleline);
        return Regex.Replace(row.Groups[1].Value, "<[^>]+>", "").Trim();
    }

    private static Task KeyAsync(RenderedComponent page, string key) =>
        page.On("[role=\"tree\"]").RaiseAsync("keydown", $"{{\"key\":\"{key}\"}}");

    [Fact]
    public async Task The_arrows_move_the_cursor_and_stop_at_the_ends()
    {
        var page = RaskTest.Render(Tree());
        Assert.Equal("src", Cursor(page.Html));

        await KeyAsync(page, "ArrowDown");
        Assert.Equal("docs", Cursor(page.Html));

        await KeyAsync(page, "ArrowUp");
        await KeyAsync(page, "ArrowUp");
        // No wrap-around: the first row is where up stops.
        Assert.Equal("src", Cursor(page.Html));

        await KeyAsync(page, "End");
        Assert.Equal("README.md", Cursor(page.Html));

        await KeyAsync(page, "ArrowDown");
        Assert.Equal("README.md", Cursor(page.Html));

        await KeyAsync(page, "Home");
        Assert.Equal("src", Cursor(page.Html));
    }

    [Fact]
    public async Task Right_opens_a_node_and_then_walks_into_it()
    {
        var page = RaskTest.Render(Tree());

        await KeyAsync(page, "ArrowRight");
        Assert.Contains("app.cs", page.Html, StringComparison.Ordinal);
        // The cursor stays on the node that opened; the second press is the one that descends.
        Assert.Equal("src", Cursor(page.Html));

        await KeyAsync(page, "ArrowRight");
        Assert.Equal("app.cs", Cursor(page.Html));
    }

    [Fact]
    public async Task Left_closes_a_node_and_then_climbs_to_its_parent()
    {
        var page = RaskTest.Render(Tree().ExpandDepth(1));
        await KeyAsync(page, "ArrowDown");
        Assert.Equal("app.cs", Cursor(page.Html));

        await KeyAsync(page, "ArrowLeft");
        Assert.Equal("src", Cursor(page.Html));

        await KeyAsync(page, "ArrowLeft");
        Assert.DoesNotContain("app.cs", page.Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Star_opens_every_sibling_that_has_children()
    {
        var page = RaskTest.Render(Tree());

        await KeyAsync(page, "*");

        Assert.Contains("app.cs", page.Html, StringComparison.Ordinal);
        Assert.Contains("tree.md", page.Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_modified_key_belongs_to_the_browser()
    {
        var page = RaskTest.Render(Tree());

        await page.On("[role=\"tree\"]").RaiseAsync("keydown", "{\"key\":\"ArrowDown\",\"ctrlKey\":true}");

        Assert.Equal("src", Cursor(page.Html));
    }

    [Fact]
    public async Task Enter_selects_one_node_at_a_time()
    {
        var picked = new List<IReadOnlyList<string>>();
        var page = RaskTest.Render(Tree().Selection(UiTreeSelection.Single).OnSelectionChange(picked.Add));

        await KeyAsync(page, "Enter");
        await KeyAsync(page, "ArrowDown");
        await KeyAsync(page, "Enter");

        Assert.Equal([["src"], ["docs"]], picked);
        Assert.Equal(1, page.Html.Split("aria-selected=\"true\"").Length - 1);
    }

    [Fact]
    public async Task Space_toggles_when_more_than_one_may_be_selected()
    {
        var picked = new List<IReadOnlyList<string>>();
        var page = RaskTest.Render(Tree().Selection(UiTreeSelection.Multiple).OnSelectionChange(picked.Add));

        await KeyAsync(page, " ");
        await KeyAsync(page, "ArrowDown");
        await KeyAsync(page, " ");
        await KeyAsync(page, " ");

        Assert.Equal(["src"], picked[0]);
        Assert.Equal(2, picked[1].Count);
        // The third press takes the second node back out, which is what "toggles" means.
        Assert.Equal(["src"], picked[2]);
    }

    [Fact]
    public async Task With_nothing_selectable_Enter_opens_the_node_instead()
    {
        var page = RaskTest.Render(Tree());

        await KeyAsync(page, "Enter");

        Assert.Contains("app.cs", page.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("aria-selected", page.Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Typing_jumps_to_the_next_node_that_starts_with_it()
    {
        var page = RaskTest.Render(Tree().NodeText(n => n.Name));

        await KeyAsync(page, "R");

        Assert.Equal("README.md", Cursor(page.Html));
    }

    [Fact]
    public async Task Without_NodeText_typing_does_nothing()
    {
        var page = RaskTest.Render(Tree());

        await KeyAsync(page, "R");

        Assert.Equal("src", Cursor(page.Html));
    }

    [Fact]
    public async Task Clicking_a_row_moves_the_cursor_and_selects_it()
    {
        var page = RaskTest.Render(Tree().Selection(UiTreeSelection.Single));

        await page.On(".ui-tree-row:has-text(\"README.md\")").ClickAsync();

        Assert.Equal("README.md", Cursor(page.Html));
        Assert.Contains("aria-selected=\"true\"", page.Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Clicking_the_twisty_only_opens_the_node()
    {
        var picked = new List<IReadOnlyList<string>>();
        var page = RaskTest.Render(Tree().Selection(UiTreeSelection.Single).OnSelectionChange(picked.Add));

        // Every row has a twisty, so it is addressed by position: the first one belongs to the first root.
        await page.InvokeAsync(page.FindAll(".ui-tree-toggle")[0].Attributes["data-rask-on-click"]!);

        Assert.Contains("app.cs", page.Html, StringComparison.Ordinal);
        Assert.Empty(picked);
    }

    [Fact]
    public async Task A_tree_that_holds_its_own_expansion_keeps_it_across_renders()
    {
        var page = RaskTest.Render(Tree());

        await KeyAsync(page, "ArrowRight");
        await KeyAsync(page, "ArrowDown");

        Assert.Contains("app.cs", page.Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_controlled_tree_reports_and_waits_to_be_told()
    {
        var reported = new List<IReadOnlyList<string>>();
        var page = RaskTest.Render(Tree().Expanded([]).OnExpandedChange(reported.Add));

        await KeyAsync(page, "ArrowRight");

        // The page holds this axis, so nothing opened — it was told what the reader asked for.
        Assert.Equal([["src"]], reported);
        Assert.DoesNotContain("app.cs", page.Html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Hovering_reports_the_node_and_leaving_reports_nothing()
    {
        var hovered = new List<string?>();
        var page = RaskTest.Render(Tree().OnHover(n => hovered.Add(n?.Name)));

        await page.On(".ui-tree-row:has-text(\"docs\")").RaiseAsync("pointerenter", "{\"pointerType\":\"mouse\"}");
        await page.On(".ui-tree-row:has-text(\"docs\")").RaiseAsync("pointerenter", "{\"pointerType\":\"mouse\"}");
        await page.On("[role=\"tree\"]").RaiseAsync("pointerleave", "{\"pointerType\":\"mouse\"}");

        // The second enter on the same row says nothing new.
        Assert.Equal(["docs", null], hovered);
    }

    [Fact]
    public async Task A_touch_is_not_a_hover()
    {
        var hovered = new List<string?>();
        var page = RaskTest.Render(Tree().OnHover(n => hovered.Add(n?.Name)));

        await page.On(".ui-tree-row:has-text(\"docs\")").RaiseAsync("pointerenter", "{\"pointerType\":\"touch\"}");

        Assert.Empty(hovered);
    }
}
