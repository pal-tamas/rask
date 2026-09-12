using Rask.Ui;

namespace Rask.Site.Features.UiKit;

/// <summary>
///     The kit's tree: what it takes, who holds its state, and what it does with a large one.
/// </summary>
/// <remarks>
///     Three trees over the same shape of data. The first holds its own state, the second hands both axes to this
///     component, and the third is 5,000 nodes with only the visible rows in the DOM.
/// </remarks>
public sealed partial class UiKitTreeDemo : Component
{
    private static readonly TreeNode[] Files =
    [
        new("src", "src", [
            new("src/app", "app", [new("src/app/Program.cs", "Program.cs"), new("src/app/App.cs", "App.cs")]),
            new("src/ui", "ui", [new("src/ui/Button.cs", "Button.cs"), new("src/ui/Card.cs", "Card.cs")]),
        ]),
        new("docs", "docs", [new("docs/tree.md", "tree.md"), new("docs/ui-kit.md", "ui-kit.md")]),
        new("README.md", "README.md"),
    ];

    private static readonly TreeNode[] Many = BuildMany();

    private IReadOnlyList<string> _open = ["src"];
    private IReadOnlyList<string> _picked = [];
    private string? _hovered;

    /// <inheritdoc />
    protected override Component? Render() =>
    [
        Section(
            "A tree",
            "Four steps and an indexer: where it starts, what identifies a node, what a node looks like, what the "
            + "control is called — and, in the brackets, what lies below a node. This one remembers what is open and "
            + "what is selected, and ExpandDepth(1) opens the roots to start with. Click a row, or focus the tree and "
            + "press the arrow keys; typing a letter jumps.",
            Div.Data(Testid("ui-tree-basic")).Class("max-w-sm rounded-xl border border-base-300 bg-base-100 p-2")[
                UiTree.Roots(Files)
                    .NodeKey(f => f.Id)
                    .Item(f => Span[f.Name])
                    .Label("Files")
                    .ExpandDepth(1)
                    .NodeText(f => f.Name)
                    .Selection(UiTreeSelection.Single)[f => f.Children]
            ]),

        Section(
            "Held by the page",
            "The same tree with both axes on this component: Expanded and Selected are fields here, and the tree "
            + "reports every change. Either axis can be the page's on its own — this one takes both so the state is "
            + "visible below.",
            Div.Data(Testid("ui-tree-controlled")).Class("max-w-sm space-y-2")[
                Div.Class("rounded-xl border border-base-300 bg-base-100 p-2")[
                    UiTree.Roots(Files)
                        .NodeKey(f => f.Id)
                        .Item(f => Span[f.Name])
                        .Label("Files, held by the page")
                        .Expanded(_open)
                        .OnExpandedChange(keys => _open = keys)
                        .Selection(UiTreeSelection.Multiple)
                        .Selected(_picked)
                        .OnSelectionChange(keys => _picked = keys)[f => f.Children]
                ],
                P.Data(Testid("ui-tree-controlled-state")).Class("text-xs text-ui-muted")[
                    $"open: {string.Join(", ", _open)} · selected: "
                    + (_picked.Count == 0 ? "none" : string.Join(", ", _picked))
                ]
            ]),

        Section(
            "Five thousand nodes",
            "ItemSize turns the tree into a virtualized flat list: one row's height, only the visible rows in the "
            + "DOM, and the levels carried by aria-level rather than by nesting. OnHover reports the node under the "
            + "pointer.",
            Div.Data(Testid("ui-tree-virtual")).Class("max-w-sm space-y-2")[
                Div.Class("rounded-xl border border-base-300 bg-base-100 p-2")[
                    UiTree.Roots(Many)
                        .NodeKey(n => n.Id)
                        .Item(n => Span[n.Name])
                        .Label("Generated nodes")
                        .ExpandDepth(1)
                        .ItemSize(28)
                        .Height(320)
                        .OnHover(n => _hovered = n?.Name)[n => n.Children]
                ],
                P.Data(Testid("ui-tree-hover-state")).Class("text-xs text-ui-muted")[
                    _hovered is null ? "hovering: nothing" : $"hovering: {_hovered}"
                ]
            ])
    ];

    // 100 branches of 50, so the flat list is long enough for the window to matter.
    private static TreeNode[] BuildMany()
    {
        var roots = new TreeNode[100];
        for (var i = 0; i < roots.Length; i++)
        {
            var children = new TreeNode[50];
            for (var j = 0; j < children.Length; j++)
            {
                children[j] = new TreeNode($"n-{i}-{j}", $"node {i}.{j}");
            }

            roots[i] = new TreeNode($"n-{i}", $"branch {i}", children);
        }

        return roots;
    }

    private static Dictionary<string, string?> Testid(string value) => new() { ["testid"] = value };

    private static Component Section(string heading, string blurb, Component body) =>
        Div.Key(heading).Class("mb-8")[
            H2.Class("text-lg font-semibold tracking-tight")[heading],
            P.Class("mt-1 mb-3 text-sm text-ui-muted")[blurb],
            body
        ];

    private sealed record TreeNode(string Id, string Name, TreeNode[]? Children = null);
}
