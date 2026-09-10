# Building components — the chain

Markup in Rask is C#. You name a component and chain onto it; there is no `new`, no factory call, and no
`using` to remember.

```csharp
Div.Class("panel")[
    H2.Class("mb-1 text-lg font-semibold")["Products"],
    P["Everything we sell."]
]
```

The `[…]` is an indexer, not a collection initializer, so the last child takes no trailing comma.

The name is the component. `Div` *is* a `Div`, so `.` shows every property it has — its own and the whole
inherited HTML surface. Children go in the indexer.

## Components that need something first

Some components cannot exist until you have told them something. A form control does not know what type it
binds until you say; a toast has no message until you give it one. Those properties are **steps** rather
than setters, and the chain asks for them first:

```csharp
BsToast.Id(7).Message("Saved").Delay(3000)
```

`Id` and `Message` are required, so they come first — in either order. Everything optional follows. Miss
one and there is nothing to render: the component does not exist yet, so the mistake is a compile error at
the point you made it, not a null at runtime.

## Bound and controlled

A form control is either **bound** to a model expression or **controlled** by a value you hold. You choose
at the first step:

```csharp
Input.Bind(() => _form.Name).Validate(ProductName.Check).Id("name")   // bound
Input.Value(_text).OnChange(v => _text = v)                           // controlled
```

**The two openings are mutually exclusive**, and the compiler still says so. They live on the chain's
entry and nowhere else, so taking one hands back the control itself and the other is simply not a member
of it. A control bound to an expression *and* handed a value would have two sources of truth with nothing
to decide between them, and the chain cannot express it.

Everything after the opening is an ordinary step on the control, in any order — `Validate`, `AfterBind`,
`Checked`, `OnInput`, `OnChange`, `Placeholder`, `Type`, `Required`, and the whole `Class`/`Id`/`Aria`
element surface.

That is a change: each mode's steps used to be declared only on that mode, so a `Validate` on a
controlled chain did not compile. The chain carried a `Bound` or `Controlled` type argument to arrange
it, and that type argument had to be threaded through every generated signature, forced the ordering
rules that came with it, and kept accumulating members that were silently unreachable. What it bought is
narrower than it looks: choosing a mode is still enforced, because the openings are exclusive. What is
no longer enforced is which steps *follow* one — a `Validate` on a controlled control compiles and does
nothing, as an unread property always could.

Both spellings infer the type from what you passed, so `Input<string>()` is never needed. Where the value
alone cannot say — `null` names no type — write it once:

```csharp
Input.Value<string>(null).Placeholder("Anything")
```

## Two things to settle

A few components need more than one fact before they exist. `UiSelect` binds a value *and* offers
options:

```csharp
UiSelect.Bind(() => _m.Country)                       // T — what the model holds, and the mode
        .Options([("hu", "Hungary"), ("gb", "UK")])   // the values and the words shown
        .Label("Country")
```

**The opening step is the one that pins the type argument**, and for a form control it is also where the
mode is chosen: `Bind` opens a bound control, `Value` a controlled one, and the two are mutually
exclusive because a control with both would have two sources of truth for one field. Everything else —
`Label`, `Options`, `Placeholder` — follows in any order, because none of them says anything about `T`.

That is a language constraint rather than a house rule: a step whose type mentions `T` cannot be
written before something has said what `T` is.

## What the IDE shows you

- Typing a component name and `.` on an ordinary component lists **every** setter it has.
- On a component with something outstanding, it lists **only what is still missing** — which is the answer
  to "how do I start?" rather than a hundred properties you cannot use yet.
- Once nothing is outstanding you get the component, and with it the full surface and the `[…]` indexer.

The intermediate types you may glimpse — `RaskSeed_…`, `RaskStage_…`, `RaskPending_…` — are generated
machinery. They are hidden from completion and never written by hand.

## Callbacks

Callbacks are ordinary properties, set like any other:

```csharp
Button.OnClick(Save)["Save"]
BsToast.Id(1).Message("Saved").OnClose(() => _open = false)
```

A callback property on a component you write is an ordinary delegate — nothing to wrap, nothing to
learn:

```csharp
public Action? OnPick { get; set; }
public Func<Task>? OnSaveAsync { get; set; }
public Action<int>? OnRate { get; set; }
public Func<Product, Component>? Template { get; set; }
```

The chain's receiver is `Build<TComponent>` rather than the component, so `.OnPick(fn)` resolves to the
setter and not to invoking the property — which is what a delegate-typed property on the receiver would
have meant (CS1593). Call one back the way you call any delegate: `OnPick?.Invoke()`.

## Your own components

Nothing above is special to the framework's components. A component you write gets the same surface:

```csharp
public sealed partial class ProductCard : Component
{
    public required string Title { get; set; }   // a step
    public string? Subtitle { get; set; }        // a setter
    public Action? OnPick { get; set; }

    protected override Component? Render() => …;
}

ProductCard.Title("Coffee").Subtitle("Dark roast").OnPick(Pick)
```

A non-nullable property with no initializer is required — the same rule
[RASK001](diagnostics.md#rask001) describes — so it becomes a step. Give it a nullable type or an
initializer if it is genuinely optional.

## Lists of components

A chain that ends at the `[...]` children indexer is already a component, so projecting one has always
worked:

```csharp
Tbody[rows.Select(r => Tr.Key(r.Id)[Td[r.Name], Td[r.Total]])]
```

A chain that ends at a **step** is different. `OpsBadge.Key(k).Label(v)` has the type
`Build<OpsBadge>`, and the implicit conversion that makes it a component at a call site does not lift
through `IEnumerable<>` — so a projection of those is not a sequence of components. The children
indexer accepts it anyway:

```csharp
Div[scopes.Select(s => OpsBadge.Key(s.Key).Label($"{s.Key}={s.Value}"))]
```

Literals and a projection can also sit in one list, which saves a `Concat`:

```csharp
Div["Showing ", rows.Select(r => Row.Key(r.Id).For(r)), " of ", total]
```

Nested sequences flatten, so `SelectMany` is optional, and a sequence of plain values renders as text
exactly as a literal child does.

This last overload takes `object?`, so it is the one place in the chain where a mistake is not a
compile error: an element that is neither a component, nor a chain, nor a value with a text
representation throws while rendering, naming the type. A generic indexer cannot express the typed
version — C# forbids generic indexers, and a C# 14 extension block cannot declare one either
(`CS9282`).

Keys still matter. [RASK022](diagnostics.md#rask022) reads a chain that ends at a step, so a list item
without `.Key(…)` is reported in every one of the shapes above.

## See also

- [Composition](composition.md) — context, callbacks, and passing components around.
- [Forms](forms.md) — binding, validation, and the form controls in full.
- [Diagnostics](diagnostics.md) — RASK001 and RASK038, which are the rules above stated as
  errors.
