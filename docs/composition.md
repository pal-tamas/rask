# Composition: context, callbacks, children & lists

How Rask components talk to each other — passing data down without prop drilling,
sending events up, nesting children, and rendering windowed or reorderable lists.

## On this page

- [Children & fragments](#children--fragments)
- [Component tiers: static method · stateless · stateful](#component-tiers-static-method--stateless--stateful)
- [Hosting a component you built yourself](#hosting-a-component-you-built-yourself)
- [Callbacks & context](composition-callbacks-context.md) — child→parent callbacks, provide/consume context.
- [Lists, toasts, drag & error boundaries](composition-lists.md) — virtualize, keyed lists, toasts, drag-and-drop, error boundaries.

---

## Children & fragments

Children attach through the **indexer**, not a `Children:` parameter:

```csharp
Div.Class("panel")[
    Span.Class("title")["Hello"],
    "plain text becomes a Text node",   // string → Text (HTML-encoded)
    items.Select(i => (Component)Li.Key(i.Id)[i.Name])
]
```

> **Keys must be unique among siblings.** `.Key(…)` is the reconciliation identity the live diff
> uses to move a row instead of rebuilding it. If two siblings share a key, keyed
> reconciliation can't tell them apart, so the diff falls back to a positional walk that may
> attach a row's DOM state (focus, input value, scroll) to the wrong sibling on reorder. The
> diff codec reports a one-time `data-rask-key="…"` error (via the diagnostics seam) when it detects
> a duplicate — treat it as a bug to fix, not noise.

> **For a component, `Key` also decides which instance is reused.**
> A keyed component is identified by its key rather than by its position among its siblings, which
> is what keeps the state it holds *itself* — a private field, an edit buffer, an open/closed
> toggle, a subscription taken in `Mount` — with the item rather than with the slot when the list
> changes shape. Where on the chain you write it does not matter: steps written before `Key` are
> carried onto the instance the key keeps.
>
> ```csharp
> TodoRow.Key(item.Id).Item(item)   // reads best: which item, then what about it
> TodoRow.Item(item).Key(item.Id)   // the same thing
> ```
>
> `Key` is available before a component's *required* steps too, generic ones included
> (`UiSelect.Key(id).Value(v)`), so a row can say which item it is first. An element is re-specified
> in full every render, so its instance carries nothing and `Div.Class("line").Key(i)` never claims one.

A `[...]` collection expression renders its items with **no wrapping element** — use it for a list
of siblings. For the conditional "render nothing" branch, return `null` (a `null` child renders
nothing):

```csharp
show ? Panel() : null    // null renders nothing
```

> The `..` spread fails inside `[…]` (the compiler parses it as a `Range`). Pass the
> enumerable directly — `Div()[items]` — instead of `Div()[..items]`.

A layout is often a fragment for the same reason — several siblings that share no wrapper:

```csharp
protected override Component? Render() =>
    [Header[H1["My app"]], Main[Outlet], Footer["Contact"]];
```

The page root is an ordinary component too: it renders into `<body>`, and Rask composes the document
around it — see [the document and the `Head` override](getting-started.md#7-the-document-and-the-head-override).

---

## Hosting a component you built yourself

Components normally enter the tree through their **generated chain**, and that chain is what
registers the instance with its parent. Occasionally you can't call one — because the type isn't known
until runtime:

```csharp
var page = (Component)ActivatorUtilities.CreateInstance(services, pluginType);
```

Put it in the tree like any other child:

```csharp
Div.Class("host")[page]
```

It gets the whole lifecycle — `Mount`, `Updated`, `FirstRender`, `Rendered`, `Unmount` — and a handle to re-render
through, exactly as a component a chain built does: the render walk notices an instance nothing registered and
registers it under the component whose subtree it sits in. Several instances of one type under one parent each
keep their own lifecycle.

> Constructing a component with `new` outside the framework is a compile error
> ([RASK014](diagnostics.md#rask014)) — a type you can name, you build through its chain. Reflection-built
> instances, whose type the compiler never sees, are the case this is for.

---

## Component tiers: static method · stateless · stateful

There are **three ways** to author a reusable unit of UI, in ascending order of cost and
capability. Reach for the cheapest one that does the job.

<!-- demo:component-tiers -->

**Tier 0 — a plain static method.** Just a function that returns markup:

```csharp
internal static class Ui
{
    public static Component Badge(string label) => Span.Class("pill")[label];
}
// call it like any method — Ui.Badge("new")
```

There is no `Component` instance, so it has **no state, no lifecycle hooks, and no independent
render cache** — its markup is inlined into whichever component calls it, on every render of that
caller. It is the leanest way to factor out repeated markup. Two things it *cannot* do: hold
mutable state, and safely consume ambient context — a static helper has no instance to carry the
"re-run me when context changes" latch, so a `Context.Get<T>()` inside one only refreshes when its
caller happens to re-render. The moment you need either, promote to Tier 1.

**Tier 1 — a stateless component.** A `Component` subclass whose `Render()` is a pure function of
its props, with **no mutable fields**:

```csharp
public sealed partial class Greeting : Component
{
    public required string Name { get; set; }   // non-nullable, no initializer → a required chain step
    protected override Component? Render() => P["Hello, ", Strong[Name], "!"];
}
// name it and chain — Greeting.Name("Ada")
```

Public settable props become chain steps and setters (see the rules in
[the README](../README.md) and [lifecycle.md](lifecycle.md)). Over a static method it gains a
reconciliation identity, the [lifecycle hooks](lifecycle.md), render caching, `<head>`
contribution, and safe context reads — it simply carries no local state.

**Tier 2 — a stateful component.** A `Component` subclass that keeps local state in **private
fields** and mutates them in handlers:

```csharp
public sealed partial class Counter : Component
{
    private int _count;
    protected override Component? Render() =>
        Button.OnClick(() => _count++)[$"Clicked {_count} times"];
}
```

The instance persists across renders (Rask reconciles it by `(type, sibling-position)` or by an
explicit `Key`), so the field survives. The `OnClick` lambda captures `this`, so after it runs the
framework re-renders this component **automatically — no `StateHasChanged()`** (the same auto-wrap
that powers [callbacks](composition-callbacks-context.md#callbacks-child--parent)). You only call `StateHasChanged()` when the
mutation happens *off* the event-dispatch path (e.g. a background poll loop — see
[lifecycle.md](lifecycle.md)).

| Tier | You write | State | Lifecycle | Render cache | Chain | Context reads |
|------|-----------|-------|-----------|--------------|---------|----------------|
| **0 · static method** | `static Component Foo(…)` | none (inlined) | none | none | none — call it | ⚠️ only refreshes with the caller |
| **1 · stateless component** | subclass, props → `Render()`, no fields | none | ✅ | ✅ | ✅ generated | ✅ |
| **2 · stateful component** | subclass with private fields | private fields | ✅ | ✅ | ✅ generated | ✅ |

**Rule of thumb:** start with a static method; promote to a stateless component when you need an
identity, lifecycle, `<head>` assets, context-driven re-render, or a clean chain API; promote to
a stateful component when you need mutable local state.

---

See also: [Lifecycle](lifecycle.md) for when `Updated` refires, and
[JS interop](js-interop.md) for element refs and scoped JS.
