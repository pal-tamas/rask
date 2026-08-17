# Building components — the chain

Markup in Rask is C#. You name a component and chain onto it; there is no `new`, no factory call, and no
`using` to remember.

```csharp
Div.Class("card")[
    H2.Class("card-title")["Products"],
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
at the first step, and the choice is the type:

```csharp
Input.Bind(() => _form.Name).Validate(ProductName.Check).Id("name")   // bound
Input.Value(_text).OnChange(v => _text = v)                           // controlled
```

Having picked one, the other is not offered. A control bound to an expression *and* handed a value has two
sources of truth and nothing decides which wins — so the surface does not let you write it.

Both spellings infer the type from what you passed, so `Input<string>()` is never needed. Where the value
alone cannot say — `null` names no type — write it once:

```csharp
Input.Value<string>(null).Placeholder("Anything")
```

## Two things to settle

A few components need more than one fact before they exist. `BsSelect` binds a value *and* offers options,
and the options need not be the values:

```csharp
BsSelect.Bind(() => _m.TeamId)      // TValue — what the model holds
        .Options(Teams)              // TItem  — what the list contains
        .OptionValue(t => t.Id)      // how one becomes the other
        .Label("Team")
```

When the option **is** the value, say so by passing a matching list and the projection is filled in:

```csharp
BsSelect.Bind(() => _m.Plan).Options(Plans)
```

The order here is fixed, and by the language rather than by us: `OptionValue` is a `Func<TItem, TValue>`,
so it cannot be written before the `Options` that says what `TItem` is.

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

## See also

- [Composition](composition.md) — context, callbacks, and passing components around.
- [Forms](forms.md) — binding, validation, and the form controls in full.
- [Diagnostics](diagnostics.md) — RASK001 and RASK038, which are the rules above stated as
  errors.
