namespace Rask.Ui.Tests.Components;

/// <summary>
///     Which nodes a tree shows, in which order, and what each row knows about its place — the arithmetic a tree control
///     actually goes wrong in.
/// </summary>
public sealed class UiTreeNavTests
{
    private sealed record Node(string Id, params Node[] Kids);

    private static readonly Node[] Tree =
    [
        new("a", new Node("a1"), new Node("a2", new Node("a2x"))),
        new("b"),
    ];

    private static List<UiTreeRow<Node, string>> Flatten(params string[] expanded) =>
        UiTreeNav.Flatten(Tree, n => n.Id, n => n.Kids, [.. expanded], []);

    [Fact]
    public void Collapsed_shows_only_the_roots()
    {
        Assert.Equal(["a", "b"], Flatten().Select(r => r.Key));
    }

    [Fact]
    public void Expanding_a_node_puts_its_children_under_it()
    {
        Assert.Equal(["a", "a1", "a2", "b"], Flatten("a").Select(r => r.Key));
    }

    [Fact]
    public void Only_the_expanded_branch_descends()
    {
        // "a2" is expanded too, but it is not visible while "a" is closed, so neither is its child.
        Assert.Equal(["a", "b"], Flatten("a2").Select(r => r.Key));
        Assert.Equal(["a", "a1", "a2", "a2x", "b"], Flatten("a", "a2").Select(r => r.Key));
    }

    [Fact]
    public void A_row_knows_its_level_parent_and_place_among_its_siblings()
    {
        var rows = Flatten("a", "a2");

        Assert.Equal([1, 2, 2, 3, 1], rows.Select(r => r.Level));
        Assert.Equal([-1, 0, 0, 2, -1], rows.Select(r => r.Parent));
        Assert.Equal([2, 2, 2, 1, 2], rows.Select(r => r.SetSize));
        Assert.Equal([1, 1, 2, 1, 2], rows.Select(r => r.PosInSet));
    }

    [Fact]
    public void A_parent_says_so_and_a_leaf_does_not()
    {
        var rows = Flatten("a");

        Assert.Equal([true, false, true, false], rows.Select(r => r.HasChildren));
        Assert.Equal([true, false, false, false], rows.Select(r => r.IsExpanded));
    }

    [Fact]
    public void The_index_names_every_visible_row()
    {
        var index = new Dictionary<string, int>();

        var rows = UiTreeNav.Flatten(Tree, n => n.Id, n => n.Kids, ["a"], index);

        Assert.Equal(rows.Count, index.Count);
        Assert.Equal(2, index["a2"]);
    }

    [Fact]
    public void A_key_seen_twice_is_rendered_once_and_not_counted_among_its_siblings()
    {
        var twin = new Node("same");
        Node[] roots = [twin, twin, new Node("other")];

        var rows = UiTreeNav.Flatten(roots, n => n.Id, n => n.Kids, [], []);

        Assert.Equal(["same", "other"], rows.Select(r => r.Key));
        Assert.Equal([2, 2], rows.Select(r => r.SetSize));
    }

    [Fact]
    public void A_cycle_ends_instead_of_running_forever()
    {
        // A node that is its own child: the second sighting is the one already rendered.
        var kids = new List<Node>();
        var loop = new Node("loop");
        var rows = UiTreeNav.Flatten([loop], n => n.Id, _ => kids, ["loop"], []);
        kids.Add(loop);

        Assert.Equal(["loop"], rows.Select(r => r.Key));
        Assert.Equal(["loop"], UiTreeNav.Flatten([loop], n => n.Id, _ => kids, ["loop"], []).Select(r => r.Key));
    }

    [Fact]
    public void Children_are_asked_for_only_where_they_are_shown()
    {
        var asked = new List<string>();

        UiTreeNav.Flatten(Tree, n => n.Id, n => { asked.Add(n.Id); return n.Kids; }, ["a"], []);

        // Depth first, and never the closed branch below "a2": the walk asks a node for its children as it reaches it.
        Assert.Equal(["a", "a1", "a2", "b"], asked);
    }

    [Fact]
    public void A_subtree_ends_at_the_next_row_at_its_level_or_above()
    {
        var rows = Flatten("a", "a2");

        Assert.Equal(4, UiTreeNav.SubtreeEnd(rows, 0));
        Assert.Equal(4, UiTreeNav.SubtreeEnd(rows, 2));
        Assert.Equal(5, UiTreeNav.SubtreeEnd(rows, 4));
    }

    [Fact]
    public void Siblings_are_the_rows_that_share_a_parent()
    {
        var rows = Flatten("a");

        Assert.Equal(["a1", "a2"], UiTreeNav.Siblings(rows, 1).Select(r => r.Key));
        Assert.Equal(["a", "b"], UiTreeNav.Siblings(rows, 0).Select(r => r.Key));
    }

    [Fact]
    public void A_depth_names_the_keys_a_tree_opens_with()
    {
        Assert.Empty(UiTreeNav.KeysToDepth(Tree, (Node n) => n.Id, n => n.Kids, 0));
        Assert.Equal(["a", "b"], UiTreeNav.KeysToDepth(Tree, (Node n) => n.Id, n => n.Kids, 1).Order());
        Assert.Equal(["a", "a1", "a2", "b"], UiTreeNav.KeysToDepth(Tree, (Node n) => n.Id, n => n.Kids, 2).Order());
    }

    [Fact]
    public void Without_a_children_selector_the_roots_are_the_whole_tree()
    {
        var rows = UiTreeNav.Flatten(Tree, n => n.Id, null, ["a"], []);

        Assert.Equal(["a", "b"], rows.Select(r => r.Key));
        Assert.All(rows, r => Assert.False(r.HasChildren));
    }
}
