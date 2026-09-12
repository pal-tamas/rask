namespace Rask.Ui.Tests.Components;

/// <summary>
///     What a tree renders: one focusable element, the ARIA a reader needs, and the two shapes — nested lists, or a
///     virtualized flat list for a large one.
/// </summary>
public partial class UiTreeTests : global::Rask.Core.RaskMarkup
{
    private sealed record Node(string Id, string Name, Node[]? Kids = null);

    private static readonly Node[] Files =
    [
        new("src", "src", [new("src/a.cs", "a.cs"), new("src/b.cs", "b.cs")]),
        new("README.md", "README.md"),
    ];

    private static UiTree<Node, string> Tree() =>
        (UiTree<Node, string>)UiTree.Roots(Files)
            .NodeKey(n => n.Id)
            .Item(n => Span[n.Name])
            .Label("Files")[n => n.Kids];

    [Fact]
    public void One_focusable_element_carries_the_tree_and_its_name()
    {
        var html = Tree().ToHtml();

        Assert.Contains("role=\"tree\"", html, StringComparison.Ordinal);
        Assert.Contains("tabindex=\"0\"", html, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"Files\"", html, StringComparison.Ordinal);
        // One focusable element, not one per row.
        Assert.Equal(1, html.Split("tabindex=\"0\"").Length - 1);
    }

    [Fact]
    public void A_collapsed_tree_draws_no_group()
    {
        var html = Tree().ToHtml();

        Assert.DoesNotContain("role=\"group\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("a.cs", html, StringComparison.Ordinal);
    }

    [Fact]
    public void ExpandDepth_opens_the_roots_on_the_first_render()
    {
        var html = Tree().ExpandDepth(1).ToHtml();

        Assert.Contains("role=\"group\"", html, StringComparison.Ordinal);
        Assert.Contains("a.cs", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Controlled_expansion_opens_exactly_those_nodes()
    {
        var html = Tree().Expanded(["src"]).ToHtml();

        Assert.Contains("a.cs", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_parent_says_whether_it_is_open_and_a_leaf_says_nothing()
    {
        var html = Tree().ToHtml();

        Assert.Contains("aria-expanded=\"false\"", html, StringComparison.Ordinal);
        // Two rows, one parent: exactly one aria-expanded.
        Assert.Equal(1, html.Split("aria-expanded=").Length - 1);
    }

    [Fact]
    public void Nothing_is_selectable_until_a_mode_is_named()
    {
        Assert.DoesNotContain("aria-selected", Tree().ToHtml(), StringComparison.Ordinal);
        Assert.Contains("aria-selected", Tree().Selection(UiTreeSelection.Single).ToHtml(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_selection_handed_in_without_a_mode_means_single()
    {
        // The alternative is a tree that takes a selection and silently ignores it.
        Assert.Contains("aria-selected=\"true\"", Tree().Selected(["README.md"]).ToHtml(), StringComparison.Ordinal);
    }

    [Fact]
    public void Multiple_says_so_on_the_tree()
    {
        var html = Tree().Selection(UiTreeSelection.Multiple).ToHtml();

        Assert.Contains("aria-multiselectable=\"true\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "aria-multiselectable", Tree().Selection(UiTreeSelection.Single).ToHtml(), StringComparison.Ordinal);
    }

    [Fact]
    public void The_cursor_names_a_row_that_is_rendered()
    {
        var html = Tree().ToHtml();
        var active = Between(html, "aria-activedescendant=\"", "\"");

        Assert.False(string.IsNullOrEmpty(active), "the tree names no active row:" + Environment.NewLine + html);
        Assert.Contains("id=\"" + active + "\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Two_trees_on_a_page_do_not_share_row_ids()
    {
        var first = Between(Tree().ToHtml(), "aria-activedescendant=\"", "\"");
        var second = Between(Tree().ToHtml(), "aria-activedescendant=\"", "\"");

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Without_a_children_selector_the_roots_are_the_whole_tree()
    {
        var html = ((UiTree<Node, string>)UiTree.Roots(Files)
                .NodeKey(n => n.Id)
                .Item(n => Span[n.Name])
                .Label("Files"))
            .ExpandDepth(3)
            .ToHtml();

        Assert.DoesNotContain("role=\"group\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("aria-expanded", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Hover_is_wired_only_when_somebody_is_listening()
    {
        // A live render, like the kit's other handler tests: static HTML carries no handler ids to name.
        Assert.DoesNotContain(
            "data-rask-on-pointerenter",
            global::Rask.Testing.RaskTest.Render(Tree()).Html,
            StringComparison.Ordinal);
        Assert.Contains(
            "data-rask-on-pointerenter",
            global::Rask.Testing.RaskTest.Render(Tree().OnHover(_ => { })).Html,
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_virtualized_list_carries_the_level_the_nesting_no_longer_shows()
    {
        var html = Tree().ExpandDepth(1).ItemSize(28).Height(320).ToHtml();

        Assert.Contains("aria-level=\"1\"", html, StringComparison.Ordinal);
        Assert.Contains("aria-level=\"2\"", html, StringComparison.Ordinal);
        Assert.Contains("aria-setsize=\"2\"", html, StringComparison.Ordinal);
        Assert.Contains("aria-posinset=\"1\"", html, StringComparison.Ordinal);
        Assert.Contains("--ui-tree-depth:1", html, StringComparison.Ordinal);
        // Flat: every row is a child of the tree itself.
        Assert.DoesNotContain("role=\"group\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_virtualized_list_says_where_the_cursor_is_for_a_row_it_has_not_rendered()
    {
        var html = Tree().ItemSize(28).Height(320).ToHtml();

        Assert.Contains("data-rask-item-size=\"28\"", html, StringComparison.Ordinal);
        Assert.Contains("data-rask-active-top=\"0\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_row_height_of_nothing_is_refused_by_name()
    {
        var tree = Tree().ItemSize(0);

        var error = Assert.Throws<ArgumentOutOfRangeException>(() => tree.ToHtml());

        Assert.Contains(".ItemSize(28)", error.Message, StringComparison.Ordinal);
    }

    private static string Between(string html, string start, string end)
    {
        var from = html.IndexOf(start, StringComparison.Ordinal);
        if (from < 0)
        {
            return "";
        }

        from += start.Length;
        var to = html.IndexOf(end, from, StringComparison.Ordinal);
        return to < 0 ? "" : html[from..to];
    }
}
