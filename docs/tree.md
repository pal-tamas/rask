# Tree

`UiTree` shows a hierarchy a reader can open, walk with the keyboard and select from: a file tree, a
navigation sidebar, an org chart, the component tree in Rask's own devtools.

```csharp
UiTree.Roots(_files)
    .NodeKey(f => f.Path)
    .Item(f => Span[f.Name])
    .Label("Files")[f => f.Children]
```

Four things make a tree, and the chain asks for all four: where it starts (`Roots`), what identifies a
node (`NodeKey`), what a node looks like (`Item`) and what the control is called (`Label`). The
indexer at the end is the fifth: a node's children.

## The children are an indexer, not a step

`[f => f.Children]` reads as "and below a node come these". It is the indexer rather than a property
because the chain already uses the name `Children` for the content a component is given at its call
site, and a tree's children are a function of a node rather than a list somebody writes out.

A node whose selector returns null or an empty sequence is a leaf, and leaves keep the twisty's space
so their labels line up with their siblings'. The selector runs only for nodes that are on screen, so
a collapsed branch costs nothing.

## Expansion and selection are controlled one axis at a time

By default the tree remembers what is open and what is selected. Pass the state and its callback and
the page holds that axis instead:

```csharp
UiTree.Roots(_nodes)
    .NodeKey(n => n.Id)
    .Item(n => Span[n.Name])
    .Label("Report")
    .Expanded(_open)                       // the page holds expansion
    .OnExpandedChange(keys => _open = keys)
    .Selection(UiTreeSelection.Multiple)   // and the tree holds the selection
    [n => n.Children]
```

Either axis can be the page's or the tree's, and they do not have to agree. This is the rule
[the data grid](data-grid.md) follows for its own axes, and for the same reason: a page usually cares
about one of them.

`ExpandDepth(1)` opens the roots the first time the tree renders — a starting point for a tree that
holds its own expansion, not a running instruction. A page that passes `Expanded` already says what is
open, and `ExpandDepth` is ignored there.

## Selection

`Selection(UiTreeSelection.None | Single | Multiple)`. `None` is a tree for reading and opening;
`Single` replaces the selection with the node the reader picks; `Multiple` toggles it. Passing
`Selected` or `OnSelectionChange` without naming a mode means `Single`, because a tree handed a
selection it ignores is a silent no-op — `Selection(UiTreeSelection.None)` still turns it off.

Keys reach the callback as a list of `TKey`, the whole selection each time:

```csharp
.Selected(_picked).OnSelectionChange(keys => _picked = keys)
```

## The keyboard

The tree is one focusable element. A cursor moves inside it, and `aria-activedescendant` tells a
screen reader which row the cursor is on — the shape [`UiSelect`](ui-kit.md) uses for its listbox, and
the reason this kit still ships no JavaScript.

| Key | What it does |
| --- | --- |
| <kbd>↓</kbd> / <kbd>↑</kbd> | the next or previous node on screen |
| <kbd>Home</kbd> / <kbd>End</kbd> | the first or last node on screen |
| <kbd>PageDown</kbd> / <kbd>PageUp</kbd> | a screenful, or ten rows in a nested tree |
| <kbd>→</kbd> | opens a closed node; on an open one, moves to its first child |
| <kbd>←</kbd> | closes an open node; on a closed one, moves to its parent |
| <kbd>*</kbd> | opens every sibling of the node the cursor is on |
| <kbd>Enter</kbd> | selects; with `Selection(None)`, opens or closes a parent |
| <kbd>Space</kbd> | selects, or toggles it in `Multiple` |
| a letter | jumps to the next node whose text starts with it — see below |

A key with Ctrl, Alt or Cmd belongs to the browser or the app, and the tree ignores it. Rask's runtime
keeps these keys from scrolling the page while a tree has focus, and scrolls the cursor back into view
when it moves out of sight.

## Type-ahead

`NodeText(n => n.Name)` turns typing on: letters typed in quick succession are a prefix, and the tree
jumps to the next node whose text starts with it. Pressing the same letter again cycles through the
nodes starting with that letter rather than searching for a doubled one, and a prefix that stops
growing for half a second starts a new search. Without `NodeText`, typing does nothing.

## Large trees

Nested by default, which renders as real nested lists and prerenders without a runtime.

Set `ItemSize` — one row's height in pixels — and the tree renders a virtualized flat list instead:
only the rows on screen reach the DOM, and the levels travel in `aria-level` rather than in nesting.

```csharp
UiTree.Roots(_components)
    .NodeKey(c => c.Id)
    .Item(c => Span[c.Name])
    .Label("Component tree")
    .ItemSize(28)
    .Height(320)[c => c.Children]
```

Every row is exactly `ItemSize` tall in that mode: the window is computed from that number, so a row
that grows taller drifts out of step with the scrolling. `Height` is the viewport when virtualized and
a maximum when nested.

## Hover

`OnHover(node => …)` reports the node under the pointer, and `default` once the pointer leaves the
tree. It is wired only when set, and on the Server host each crossing is a round trip — which is what
the devtools uses to highlight the component a row names.

## Duplicate keys and cycles

A key the tree has already seen is rendered once: the second sighting is dropped, and it is not
counted among its siblings, so what a screen reader is told about set sizes stays true. That also ends
a cycle — a node that is its own descendant has been seen by the time the walk reaches it again.

## What it needs

Nested markup prerenders and reads correctly with no runtime. Opening, selecting and the keyboard are
interactions, so they need the page to be live, like every other control in the kit.

Items should not contain their own focusable controls: the tree is the one focusable element, and a
button inside a row takes the keys the cursor needs.

## See also

- [UI kit](ui-kit.md) — the rest of the components, and who owns a component's state
- [Data grid](data-grid.md) — the same controlled-or-not rule, one axis at a time
- [Accessibility](accessibility.md) — the keyboard and ARIA rules the kit holds itself to
