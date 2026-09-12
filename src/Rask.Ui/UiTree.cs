using System.Globalization;
using Rask.Core.Live;
using Rask.Core.Virtualization;

namespace Rask.Ui;

/// <summary>
///     A hierarchy a reader can open, walk with the keyboard and select from.
/// </summary>
/// <remarks>
///     <para>
///         Data-driven, so it holds a large tree: the nodes are the app's own objects, and the tree asks for a node's
///         children through the indexer —
///         <c>UiTree.Roots(files).NodeKey(f =&gt; f.Path).Item(f =&gt; Span[f.Name]).Label("Files")[f =&gt; f.Children]</c>.
///         The indexer rather than a property, because the chain reserves the name <c>Children</c> for a component's own
///         markup content, and a tree's children are a function of a node rather than a list written at the call site.
///     </para>
///     <para>
///         Expansion and selection are each controlled or the tree's own, one axis at a time: pass <see cref="Expanded" />
///         with <see cref="OnExpandedChange" /> to hold that axis, or leave them unset and the tree remembers. This is the
///         grid's rule, and it is what lets a page control the half it cares about.
///     </para>
///     <para>
///         One element is focusable — the tree itself — and a cursor moves inside it through
///         <c>aria-activedescendant</c>, which is the same shape <see cref="UiSelect{T}" /> uses for its listbox and the
///         reason this kit still ships no JavaScript. The runtime keeps the navigation keys from scrolling the page while
///         a tree has focus, and scrolls the cursor back into view when it moves out of it.
///     </para>
///     <para>
///         Nested lists by default, which prerender as real markup. Setting <see cref="ItemSize" /> renders a virtualized
///         flat list instead — one row's height, and only the visible window in the DOM — for a tree with thousands of
///         nodes, where the levels are carried by <c>aria-level</c> rather than by nesting.
///     </para>
/// </remarks>
/// <typeparam name="T">The app's node.</typeparam>
/// <typeparam name="TKey">What identifies a node.</typeparam>
public sealed partial class UiTree<T, TKey> : Component
    where TKey : notnull
{
    // Per instance, so two id-less trees on one page cannot collide: aria-activedescendant points at a row BY ID, and a
    // collision aims it at the other tree's row.
    private static int _instances;

    private readonly int _instance = Interlocked.Increment(ref _instances);

    // The tree's own halves of the two axes, used only while the page is not holding them.
    private readonly HashSet<TKey> _expanded = [];
    private readonly HashSet<TKey> _selected = [];

    // Rebuilt every render: which row a key is on, and the stable ordinal its element id is built from. The ordinal
    // survives expansion, so opening a node does not renumber the ids of everything below it.
    private readonly Dictionary<TKey, int> _index = [];
    private readonly Dictionary<TKey, int> _ids = [];

    private int _nextId;
    private Func<T, IEnumerable<T>?>? _children;
    private UiTypeAhead _typeAhead;
    private TKey? _cursorKey;
    private bool _hasCursor;
    private int _cursorHint;
    private bool _seeded;
    private bool _hovering;
    private TKey? _hovered;

    /// <summary>What identifies a node — never <c>Key</c>, which is the chain's own reconciliation identity (RASK046).</summary>
    public required Func<T, TKey> NodeKey { get; set; }

    /// <summary>What a node looks like. The row around it — the twisty, the indent, the selection — is the tree's.</summary>
    public required Func<T, Component> Item { get; set; }

    /// <summary>The tree's accessible name.</summary>
    public required string Label { get; set; }

    /// <summary>The top-level nodes.</summary>
    public IEnumerable<T>? Roots { get; set; }

    /// <summary>The expanded keys, when the page holds this axis. Unset and the tree remembers its own.</summary>
    public IReadOnlyList<TKey>? Expanded { get; set; }

    /// <summary>Raised with the whole expanded set after every expand or collapse.</summary>
    public Callback<IReadOnlyList<TKey>>? OnExpandedChange { get; set; }

    /// <summary>
    ///     How deep the tree opens the first time it renders: 1 for the roots, 0 (the default) for none.
    /// </summary>
    /// <remarks>
    ///     Only a starting point, and only for a tree that holds its own expansion — a page that passes
    ///     <see cref="Expanded" /> already says what is open.
    /// </remarks>
    public int? ExpandDepth { get; set; }

    /// <summary>What a reader may select. Unset means <see cref="UiTreeSelection.Single" /> once a selection is
    /// involved, and <see cref="UiTreeSelection.None" /> otherwise.</summary>
    public UiTreeSelection? Selection { get; set; }

    /// <summary>The selected keys, when the page holds this axis.</summary>
    public IReadOnlyList<TKey>? Selected { get; set; }

    /// <summary>Raised with the whole selection after every change.</summary>
    public Callback<IReadOnlyList<TKey>>? OnSelectionChange { get; set; }

    /// <summary>A node's text for type-ahead. Unset and typing letters does nothing.</summary>
    public Fn<T, string>? NodeText { get; set; }

    /// <summary>One row's height in pixels. Setting it renders a virtualized flat list.</summary>
    public int? ItemSize { get; set; }

    /// <summary>The scroller's height in pixels — the viewport when virtualized, a maximum when nested.</summary>
    public int? Height { get; set; }

    /// <summary>The node under the pointer, and <c>default</c> once it leaves the tree.</summary>
    public Callback<T?>? OnHover { get; set; }

    /// <summary>The row density, as on a menu.</summary>
    public UiSize? Size { get; set; }

    /// <summary>Extra classes for the tree element.</summary>
    public string? Class { get; set; }

    // The clock the type-ahead's prefix expires by. Internal, so it is not a chain step; a test hands it its own.
    internal TimeProvider Clock { get; set; } = TimeProvider.System;

    // Cursor, expansion, selection and the id map are FIELDS, which the render cache cannot see. Same reason as the grid.
    /// <inheritdoc />
    protected override bool BypassRenderCache => true;

    /// <summary>
    ///     A node's children: <c>UiTree.Roots(files).NodeKey(…).Item(…).Label("Files")[f =&gt; f.Children]</c>.
    /// </summary>
    /// <remarks>
    ///     Stored, not called, like the grid's columns: it runs inside the render walk, for the nodes that are on screen,
    ///     so a collapsed branch costs nothing and a lazily-built structure is only asked about where it is shown.
    /// </remarks>
    /// <param name="children">A node's children, or null or empty for a leaf.</param>
    // An indexer is called `Item` in metadata, and this component already has an `Item` — what a node looks like. The
    // call site is unaffected: an indexer is written with brackets, never by name.
    [System.Runtime.CompilerServices.IndexerName("TreeChildren")]
    public Component this[Func<T, IEnumerable<T>?> children]
    {
        get
        {
            _children = children;
            return this;
        }
    }

    private UiTreeSelection Mode =>
        Selection ?? (Selected is not null || OnSelectionChange is not null ? UiTreeSelection.Single : UiTreeSelection.None);

    private string Prefix => "uitree-" + _instance.ToString(CultureInfo.InvariantCulture);

    private int Page => ItemSize is { } size && Height is { } height && size > 0 ? Math.Max(1, height / size) : 10;

    /// <inheritdoc />
    protected override Component? Render()
    {
        if (ItemSize is { } size && size <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ItemSize),
                ItemSize,
                "A tree's ItemSize is one row's height in pixels: .ItemSize(28). Leave it unset for nested lists.");
        }

        SeedExpansion();
        var expanded = Expanded is { } controlled ? [.. controlled] : _expanded;
        _index.Clear();
        var rows = UiTreeNav.Flatten(Roots, NodeKey, _children, expanded, _index);
        PruneIds();
        var cursor = ResolveCursor(rows);

        return ItemSize is { } rowHeight ? Flat(rows, cursor, rowHeight) : Nested(rows, cursor);
    }

    // ---- rendering ------------------------------------------------------------------------------

    // Every element step goes on BEFORE the children: the indexer ends the chain, and what it hands back is a component
    // rather than the element, so a step written after it would have nothing to attach to.
    private Component Nested(List<UiTreeRow<T, TKey>> rows, int cursor)
    {
        var tree = Ul
            .Id(Prefix)
            .Role("tree")
            .TabIndex(0)
            .Class(UiClass.Compose("menu ui-tree w-full", Size is { } s ? UiClassNames.MenuSize(s) : null, Class))
            .Aria(TreeAria(rows, cursor))
            .OnKeyDown(e => OnKeyAsync(e, rows));

        if (Height is { } height)
        {
            tree = tree.Style("max-height:" + Px(height) + ";overflow-y:auto");
        }

        if (OnHover is not null)
        {
            tree = tree.OnPointerLeave(LeaveAsync);
        }

        return tree[Branch(rows, 0, rows.Count, cursor)];
    }

    // Depth first over the flat rows: each node's subtree is the span up to the next row at its level or above.
    private IEnumerable<Component> Branch(List<UiTreeRow<T, TKey>> rows, int from, int to, int cursor)
    {
        var at = from;
        while (at < to)
        {
            var row = rows[at];
            var end = UiTreeNav.SubtreeEnd(rows, at);
            var children = row.IsExpanded
                ? [Ul.Role("group")[Branch(rows, at + 1, end, cursor)]]
                : Array.Empty<Component>();

            yield return Li
                .Key(row.Key)
                .Id(RowId(row.Key))
                .Role("treeitem")
                .Aria(RowAria(row, level: false))[
                    [Row(row, at == cursor), .. children]
                ];

            at = end;
        }
    }

    private Component Flat(List<UiTreeRow<T, TKey>> rows, int cursor, int rowHeight)
    {
        var height = Height ?? 320;
        return Virtualize.Items<UiTreeRow<T, TKey>>(
            ctx =>
            {
                var tree = Ul
                    .Id(Prefix)
                    .Role("tree")
                    .TabIndex(0)
                    .Class(UiClass.Compose(
                        "menu ui-tree ui-tree-flat w-full flex-nowrap overflow-y-auto p-0",
                        Size is { } s ? UiClassNames.MenuSize(s) : null,
                        Class))
                    .Style("height:" + Px(height) + ";--ui-tree-row:" + Px(rowHeight))
                    // What the runtime's scroll-follow needs to reach a row that is not rendered: where it would be.
                    .Data(new Dictionary<string, string?>
                    {
                        ["rask-item-size"] = Int(rowHeight),
                        ["rask-active-top"] = Int(Math.Max(cursor, 0) * rowHeight),
                    })
                    .Aria(TreeAria(rows, cursor))
                    .OnScroll(ctx.OnScroll)
                    .OnKeyDown(e => OnKeyAsync(e, rows));

                if (OnHover is not null)
                {
                    tree = tree.OnPointerLeave(LeaveAsync);
                }

                return tree[
                    Spacer("before", ctx.OffsetBefore),
                    ctx.VisibleItems.Select(v => FlatRow(v.Value, v.Index, cursor)),
                    Spacer("after", ctx.OffsetAfter)
                ];
            },
            rows,
            ItemSize: rowHeight,
            OverscanCount: 8,
            InitialClientHeight: height);
    }

    private Component FlatRow(UiTreeRow<T, TKey> row, int at, int cursor) =>
        Li
            .Key(row.Key)
            .Id(RowId(row.Key))
            .Role("treeitem")
            // The depth is a CSS variable rather than a class: a built class name is invisible to Tailwind's scan.
            .Style("--ui-tree-depth:" + Int(row.Level - 1))
            .Data(new Dictionary<string, string?> { ["rask-key"] = RowId(row.Key) })
            .Aria(RowAria(row, level: true))[
                Row(row, at == cursor)
            ];

    private static Component Spacer(string which, int height) =>
        Li.Key("ui-tree-" + which).Role("none").Style("height:" + Px(height));

    private Component Row(UiTreeRow<T, TKey> row, bool isCursor)
    {
        var content = Div
            .Id(RowId(row.Key) + "-l")
            .Class(UiClass.Compose(
                "ui-tree-row flex items-center gap-1",
                IsSelected(row.Key) ? "menu-active" : null,
                isCursor ? "ui-tree-cursor" : null))
            .OnClick(() => ClickAsync(row));

        // On the row's own box rather than on the <li>: the runtime drops an enter whose pointer came from inside the
        // handler's element, and a child row sits inside its parent's <li> — so a parent would never hear the pointer
        // come back to it. The boxes do not nest.
        if (OnHover is not null)
        {
            content = content.OnPointerEnter(e => HoverAsync(e, row));
        }

        return content[Twisty(row), Item(row.Node)];
    }

    // A leaf keeps the same box, so its label lines up with its siblings' labels rather than with their twisties.
    private Component Twisty(UiTreeRow<T, TKey> row)
    {
        var box = Span.Class("ui-tree-toggle").Aria(new Dictionary<string, string?> { ["hidden"] = "true" });
        return row.HasChildren
            ? box.OnClick(() => ToggleAsync(row))[
                UiIcon.Name(row.IsExpanded ? UiIconName.ChevronDown : UiIconName.ChevronRight)
                    .Class("size-3 shrink-0 opacity-60")
            ]
            : box;
    }

    private Dictionary<string, string?> TreeAria(List<UiTreeRow<T, TKey>> rows, int cursor)
    {
        var aria = new Dictionary<string, string?> { ["label"] = Label };
        if (cursor >= 0 && cursor < rows.Count)
        {
            aria["activedescendant"] = RowId(rows[cursor].Key);
        }

        if (Mode == UiTreeSelection.Multiple)
        {
            aria["multiselectable"] = "true";
        }

        return aria;
    }

    // aria-level and its neighbours only in the flat list: nested lists say the same thing by their structure, and a
    // reader that has both trusts the markup.
    private Dictionary<string, string?> RowAria(UiTreeRow<T, TKey> row, bool level)
    {
        var aria = new Dictionary<string, string?> { ["labelledby"] = RowId(row.Key) + "-l" };
        if (row.HasChildren)
        {
            aria["expanded"] = row.IsExpanded ? "true" : "false";
        }

        if (Mode != UiTreeSelection.None)
        {
            aria["selected"] = IsSelected(row.Key) ? "true" : "false";
        }

        if (level)
        {
            aria["level"] = Int(row.Level);
            aria["setsize"] = Int(row.SetSize);
            aria["posinset"] = Int(row.PosInSet);
        }

        return aria;
    }

    // ---- the keyboard ---------------------------------------------------------------------------

    private async Task OnKeyAsync(KeyboardEventArgs e, List<UiTreeRow<T, TKey>> rows)
    {
        // A modified key belongs to the browser or the app, not to the tree.
        if (rows.Count == 0 || e.Ctrl || e.Alt || e.Meta)
        {
            return;
        }

        var at = Math.Clamp(ResolveCursor(rows), 0, rows.Count - 1);
        var row = rows[at];
        switch (e.Key)
        {
            case "ArrowDown":
                MoveTo(rows, Math.Min(at + 1, rows.Count - 1));
                break;
            case "ArrowUp":
                MoveTo(rows, Math.Max(at - 1, 0));
                break;
            case "Home":
                MoveTo(rows, 0);
                break;
            case "End":
                MoveTo(rows, rows.Count - 1);
                break;
            case "PageDown":
                MoveTo(rows, Math.Min(at + Page, rows.Count - 1));
                break;
            case "PageUp":
                MoveTo(rows, Math.Max(at - Page, 0));
                break;
            case "ArrowRight":
                if (row.HasChildren && !row.IsExpanded)
                {
                    await SetExpandedAsync([row.Key], true).ConfigureAwait(false);
                }
                else if (row.IsExpanded)
                {
                    MoveTo(rows, at + 1);
                }

                break;
            case "ArrowLeft":
                if (row.IsExpanded)
                {
                    await SetExpandedAsync([row.Key], false).ConfigureAwait(false);
                }
                else if (row.Parent >= 0)
                {
                    MoveTo(rows, row.Parent);
                }

                break;
            case "*":
                await SetExpandedAsync(
                        [.. UiTreeNav.Siblings(rows, at).Where(r => r.HasChildren).Select(r => r.Key)], true)
                    .ConfigureAwait(false);
                break;
            case "Enter":
                await ActivateAsync(row, enter: true).ConfigureAwait(false);
                break;
            case " ":
                await ActivateAsync(row, enter: false).ConfigureAwait(false);
                break;
            default:
                TypeAhead(e.Key, at, rows);
                break;
        }
    }

    private void TypeAhead(string key, int at, List<UiTreeRow<T, TKey>> rows)
    {
        if (NodeText is not { } text || key.Length != 1)
        {
            return;
        }

        var texts = new string?[rows.Count];
        for (var i = 0; i < rows.Count; i++)
        {
            texts[i] = text.Invoke(rows[i].Node);
        }

        var hit = _typeAhead.Next(key, at, texts, Clock);
        if (hit >= 0)
        {
            MoveTo(rows, hit);
        }
    }

    // Enter and Space differ in one place only: with nothing selectable, Enter is the second way to open a branch.
    private Task ActivateAsync(UiTreeRow<T, TKey> row, bool enter) => Mode switch
    {
        UiTreeSelection.Single => CommitSelectionAsync([row.Key]),
        UiTreeSelection.Multiple => ToggleSelectionAsync(row.Key),
        _ => enter && row.HasChildren ? SetExpandedAsync([row.Key], !row.IsExpanded) : Task.CompletedTask,
    };

    private Task ClickAsync(UiTreeRow<T, TKey> row)
    {
        _cursorKey = row.Key;
        _hasCursor = true;
        _cursorHint = _index.TryGetValue(row.Key, out var at) ? at : 0;
        return ActivateAsync(row, enter: false);
    }

    private Task ToggleAsync(UiTreeRow<T, TKey> row) => SetExpandedAsync([row.Key], !row.IsExpanded);

    // ---- the two axes ---------------------------------------------------------------------------

    private void SeedExpansion()
    {
        if (_seeded)
        {
            return;
        }

        _seeded = true;
        if (Expanded is null && ExpandDepth is { } depth && depth > 0)
        {
            foreach (var key in UiTreeNav.KeysToDepth(Roots, NodeKey, _children, depth))
            {
                _expanded.Add(key);
            }
        }
    }

    private Task SetExpandedAsync(IReadOnlyList<TKey> keys, bool on)
    {
        var next = Expanded is { } controlled ? new HashSet<TKey>(controlled) : new HashSet<TKey>(_expanded);
        foreach (var key in keys)
        {
            if (on)
            {
                next.Add(key);
            }
            else
            {
                next.Remove(key);
            }
        }

        // Ours to hold only while the page is not holding it.
        if (Expanded is null)
        {
            _expanded.Clear();
            foreach (var key in next)
            {
                _expanded.Add(key);
            }
        }

        return Raise(OnExpandedChange, (IReadOnlyList<TKey>)[.. next]);
    }

    private bool IsSelected(TKey key) =>
        Mode != UiTreeSelection.None && (Selected is { } controlled ? controlled.Contains(key) : _selected.Contains(key));

    private Task ToggleSelectionAsync(TKey key)
    {
        var next = Selected is { } controlled ? new HashSet<TKey>(controlled) : new HashSet<TKey>(_selected);
        if (!next.Remove(key))
        {
            next.Add(key);
        }

        return CommitSelectionAsync([.. next]);
    }

    private Task CommitSelectionAsync(IReadOnlyList<TKey> next)
    {
        if (Selected is null)
        {
            _selected.Clear();
            foreach (var key in next)
            {
                _selected.Add(key);
            }
        }

        return Raise(OnSelectionChange, next);
    }

    // ---- the cursor, ids and hover ---------------------------------------------------------------

    // By KEY, so it survives everything above it opening or closing; by its last position when the key is gone, which is
    // what a reader means by "where I was" after the node they were on disappeared.
    private int ResolveCursor(List<UiTreeRow<T, TKey>> rows)
    {
        if (rows.Count == 0)
        {
            return -1;
        }

        if (_hasCursor && _cursorKey is { } key && _index.TryGetValue(key, out var at))
        {
            _cursorHint = at;
            return at;
        }

        if (_hasCursor)
        {
            var clamped = Math.Clamp(_cursorHint, 0, rows.Count - 1);
            _cursorKey = rows[clamped].Key;
            return clamped;
        }

        for (var i = 0; i < rows.Count; i++)
        {
            if (IsSelected(rows[i].Key))
            {
                return i;
            }
        }

        return 0;
    }

    private void MoveTo(List<UiTreeRow<T, TKey>> rows, int at)
    {
        at = Math.Clamp(at, 0, rows.Count - 1);
        _cursorKey = rows[at].Key;
        _hasCursor = true;
        _cursorHint = at;
    }

    private string RowId(TKey key)
    {
        if (!_ids.TryGetValue(key, out var ordinal))
        {
            ordinal = _nextId++;
            _ids[key] = ordinal;
        }

        return Prefix + "-n-" + ordinal.ToString(CultureInfo.InvariantCulture);
    }

    // A tree left open for an hour over changing data would otherwise keep an id for every node it ever saw.
    private void PruneIds()
    {
        if (_ids.Count <= _index.Count * 4)
        {
            return;
        }

        foreach (var key in _ids.Keys.Where(k => !_index.ContainsKey(k)).ToList())
        {
            _ids.Remove(key);
        }
    }

    private Task HoverAsync(PointerEventArgs e, UiTreeRow<T, TKey> row)
    {
        // A touch reports one enter for the tap that follows it, which is not hovering.
        if (e.PointerType == "touch"
            || (_hovering && _hovered is { } previous && EqualityComparer<TKey>.Default.Equals(previous, row.Key)))
        {
            return Task.CompletedTask;
        }

        _hovering = true;
        _hovered = row.Key;
        return Raise(OnHover, (T?)row.Node);
    }

    private Task LeaveAsync(PointerEventArgs e)
    {
        if (!_hovering)
        {
            return Task.CompletedTask;
        }

        _hovering = false;
        _hovered = default;
        return Raise(OnHover, default(T?));
    }

    private static Task Raise<TArg>(Callback<TArg>? handler, TArg arg) => handler?.Invoke(arg) ?? Task.CompletedTask;

    private static string Px(int value) => value.ToString(CultureInfo.InvariantCulture) + "px";

    private static string Int(int value) => value.ToString(CultureInfo.InvariantCulture);
}
