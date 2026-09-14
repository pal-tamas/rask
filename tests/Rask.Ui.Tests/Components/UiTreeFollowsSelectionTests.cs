using System.Text.RegularExpressions;
using Rask.Core;
using Rask.Testing;

namespace Rask.Ui.Tests.Components;

/// <summary>
///     A selection the page makes — a search result, a picker — takes the tree's cursor with it, which is what scrolls the
///     node into view. A selection the reader made, handed back by the page, leaves the cursor where the reader put it.
/// </summary>
public sealed class UiTreeFollowsSelectionTests
{
    [Fact]
    public async Task A_node_the_page_selects_takes_the_cursor()
    {
        var page = RaskTest.Render(new TreeSelectionHost());

        // The reader has been somewhere else first, so the cursor is theirs rather than the selection's default.
        await page.On("[role=\"tree\"]").RaiseAsync("keydown", "{\"key\":\"ArrowDown\"}");
        Assert.Equal("docs", Cursor(page.Html));

        await page.On("#select-readme").ClickAsync();

        Assert.Equal("README.md", Cursor(page.Html));
    }

    [Fact]
    public async Task A_node_inside_a_branch_the_page_opens_in_the_same_render_takes_the_cursor()
    {
        var page = RaskTest.Render(new TreeSelectionHost());
        // Without a cursor of the reader's, the tree would start on the selection anyway and prove nothing.
        await page.On("[role=\"tree\"]").RaiseAsync("keydown", "{\"key\":\"End\"}");

        await page.On("#select-deep").ClickAsync();

        Assert.Equal("tree.md", Cursor(page.Html));
    }

    [Fact]
    public async Task A_selection_the_reader_made_leaves_the_cursor_on_the_readers_row()
    {
        var page = RaskTest.Render(new TreeSelectionHost());

        await page.On(".ui-tree-row:has-text(\"README.md\")").ClickAsync();
        await page.On("[role=\"tree\"]").RaiseAsync("keydown", "{\"key\":\"Home\"}");
        // A render the page causes without touching the selection is not a new selection.
        await page.On("#noop").ClickAsync();

        Assert.Equal("src", Cursor(page.Html));
    }

    // What the tree says the cursor is on: the row its aria-activedescendant names, read back as the text on screen.
    private static string Cursor(string html)
    {
        var active = Regex.Match(html, "aria-activedescendant=\"([^\"]+)\"").Groups[1].Value;
        var row = Regex.Match(html, "id=\"" + Regex.Escape(active) + "-l\"[^>]*>(.*?)</div>", RegexOptions.Singleline);
        return Regex.Replace(row.Groups[1].Value, "<[^>]+>", "").Trim();
    }
}

/// <summary>A page holding a tree's selection and expansion, with buttons that change them from outside the tree.</summary>
public sealed partial class TreeSelectionHost : Component
{
    private static readonly FileNode[] Files =
    [
        new("src", "src", [new("src/app.cs", "app.cs")]),
        new("docs", "docs", [new("docs/tree.md", "tree.md")]),
        new("README.md", "README.md"),
    ];

    private IReadOnlyList<string> _selected = [];
    private IReadOnlyList<string> _expanded = [];
    private int _renders;

    /// <inheritdoc />
    protected override bool BypassRenderCache => true;

    /// <inheritdoc />
    protected override Component? Render() =>
    [
        Button.Id("select-readme").OnClick(() => _selected = ["README.md"])["README"],
        Button.Id("select-deep").OnClick(() =>
        {
            _expanded = ["docs"];
            _selected = ["docs/tree.md"];
        })["deep"],
        Button.Id("noop").OnClick(() => _renders++)[$"render {_renders}"],
        UiTree.Roots(Files)
            .NodeKey(n => n.Id)
            .Item(n => Span[n.Name])
            .Label("Files")
            .Expanded(_expanded)
            .OnExpandedChange(keys => _expanded = keys)
            .Selected(_selected)
            .OnSelectionChange(keys => _selected = keys)[n => n.Kids]
    ];
}

/// <summary>One node of <see cref="TreeSelectionHost" />'s tree.</summary>
public sealed record FileNode(string Id, string Name, FileNode[]? Kids = null);
