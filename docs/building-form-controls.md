# Building form controls

Rask ships a few form elements (`Input`/`Select`/`Textarea`), and this guide adds
typed, ready-made controls —
but the binding system is **public**, so you write exactly the controls your app needs. A custom control gets the same two-way binding, per-field
validation, and controlled mode as the built-ins by implementing one interface: **`IFormControl<T>`**.
The generator does the rest.

This is the end-to-end guide. For the wider forms story (binding, `HTMLFormElement<TModel>`, validation layers) see
[forms.md](forms.md).

---

## 1. The shape of a form control

A form control is a generic `Component` over its **value type `T`** that implements `IFormControl<T>`
(`Rask.Core.Forms`). The interface declares both usage modes:

```csharp
public interface IFormControl<T>
{
    // Bound mode — two-way binds an lvalue and drives the ambient EditContext.
    Expression<Func<T>>? Bind { get; set; }
    Validator<T>? Validate { get; set; }
    Callback<T> AfterBind { get; set; }

    // Controlled mode — the parent owns Value and is notified of changes.
    T? Value { get; set; }
    Callback<T> OnChange { get; set; }
}
```

**A rule and a hook are ONE property each, taking either shape.** `Validator<T>` accepts a synchronous
`Validate<T>` or an asynchronous `ValidateAsync<T>`; `Callback<T>` accepts an `Action<T>` or a
`Func<T, Task>`. There is no `…Async` sibling to choose between, so there is no pair to set both halves
of and no rule about which one wins.

Read them back through `Invoke`: `await Validate?.Invoke(v, ct)` hands back the messages, and
`await OnChange.Invoke(v);` runs the handler — nothing when none is set, and with no `Task` when it was
synchronous.

Those three are **carriers**, and the reason is the chain. A delegate-typed property is *invocable*, so
`.Validate(rule)` would bind to the property rather than to the setter of the same name (CS1593) the
moment the chain's receiver is the control itself. A carrier is not invocable, so lookup falls through to
the step — which is also what lets one step name carry an overload per shape.

You declare those five properties (plus your own display props), implement `Render`, and the generator
emits a **chain** whose entry step chooses the mode:

- **bound** — `MyControl.Bind(() => model.Field)`, where `.Validate(…)` takes either rule shape with no
  cast;
- **controlled** — `MyControl.Value(v).OnChange(…)`, or `MyControl.Of<T>()` when there is no value yet.

Either opening hands back the control itself (`MyControl<T>`), so every later step is an ordinary setter
on it. **The openings are mutually exclusive:** `Bind` and `Value` live on the chain's entry and nowhere
else, so once one is taken the other is not a member of what you hold. Bound mode owns the value and the
write-back; controlled mode parses no expression. The steps that *follow* an opening are not gated by
mode — a `Validate` on a controlled control compiles and simply is never read (see
[building-components.md](building-components.md#bound-and-controlled)).

`Of<T>()` is emitted for **every** generic form control, including one with required props of its own.
That is not an exception to the ordinary rule (which withholds `Of` where a required step already pins
the type) but the same rule read properly: a form control's openings are its *mode* pins, so a required
step of its own is never one and never gets to pin `T`. A control requiring a `Label` — which says
nothing about `T` — would otherwise have no controlled way in at all until the caller invented a value.

**A control closed over a value type gets a non-nullable `Value`.** `IFormControl<T>` declares
`T? Value`, where `?` over an unconstrained `T` is a nullability *annotation* — so `IFormControl<bool>`
has a plain `bool Value`. It is still the opening of the controlled chain and never a step outstanding
after one, in either mode: bound mode withdraws `Value` on purpose, so treating it as required would
leave `MyCheck.Bind(…).Label(…)` pending forever on a step its own mode does not offer, with no symptom
beyond the chain having no `ToHtml`.

**…and a second `Bind` opening over the nullable.** The chain of a control over a non-nullable value type
also takes `Bind(Expression<Func<T?>>)`, so `MyCheck.Bind(() => model.Agreed)` compiles over a `bool?` as well
as a `bool`. The generated overload hands the expression to `ExpressionAccessor.NonNullable`, which wraps its
body in a `Convert` that `ExpressionAccessor.Parse` strips again. Your `Bind` property therefore still holds an
`Expression<Func<bool>>`, while the accessor reads and writes the nullable property itself: the getter returns
`null` for an unset value, so read it with `accessor.Getter() is bool b && b` rather than a cast, and a boxed
`bool` written through the setter fits either property. Never compile the expression: the conversion would
throw on a `null`.

The rule is by **name**, which is what lets it reach a prop the interface does not declare. If your
control has its own `Checked` or `OnInput` — as the core `Input` and `Textarea` do — those are recognized
as controlled-mode members too, because bound mode derives the checked state from the model and installs
its own `oninput` handler and so would never read them. Any *other* property you declare is shared.

---

## 2. A complete example — `SegmentedControl<TValue>`

A single-select rendered as a row of buttons (think iOS segmented control). It binds one `TValue` chosen
from `Options`, works bound or controlled, and supports per-field validation.

```csharp
using System.Linq.Expressions;
using Rask.Core.Forms;

namespace MyApp.Controls;

public sealed partial class SegmentedControl<TValue> : Component, IFormControl<TValue>
{
    public required IEnumerable<TValue> Options { get; set; }
    // A template is an Fn — a struct, so `.OptionLabel(fn)` stays a step (a delegate-typed
    // property on the receiver would be read as an invocation, CS1593).
    public Fn<TValue, Component>? OptionLabel { get; set; }
    public string? Class { get; set; }

    // IFormControl<TValue> — controlled mode.
    public TValue? Value { get; set; }
    public Callback<TValue> OnChange { get; set; }

    // IFormControl<TValue> — bound mode.
    public Expression<Func<TValue>>? Bind { get; set; }
    public Validator<TValue>? Validate { get; set; }
    public Callback<TValue> AfterBind { get; set; }

    protected override Component? Render()
    {
        var comparer = EqualityComparer<TValue>.Default;

        // Resolve the binding once per render (bound mode only).
        ExpressionAccessor.Accessor? acc = null;
        EditContext? ctx = null;
        var fid = default(FieldIdentifier);
        TValue? current;
        if (Bind is not null)
        {
            acc = ExpressionAccessor.Parse(Bind);
            ctx = BindingHelpers.ResolveBindingContext(acc.Target);
            fid = acc.Field;
            ((IFormControl<TValue>)this).RegisterValidator(acc, ctx);   // helper — see §3
            current = acc.Getter() is TValue v ? v : default;
        }
        else
        {
            current = Value;
        }

        var buttons = new List<Component>();
        var i = 0;
        foreach (var option in Options)
        {
            var captured = option;
            var active = current is not null && comparer.Equals(captured, current);
            buttons.Add(Button.Type("button").Class(active ? "btn btn-primary" : "btn btn-outline-primary").OnClick(() => SelectAsync(acc, ctx, fid, captured)).Key(i++)[OptionLabel?.Invoke(option) ?? (Component)(option?.ToString() ?? "")]);
        }

        var children = new List<Component> { Div.Class("action-group")[buttons] };
        if (Bind is not null)
        {
            children.Add(Validation.Message.Template(msgs => Div.Class("field-error block")[msgs[0]]).For(Bind));
        }

        return Div.Class(Class ?? "segmented")[children];
    }

    private async Task SelectAsync(
        ExpressionAccessor.Accessor? acc, EditContext? ctx, FieldIdentifier fid, TValue value)
    {
        var self = (IFormControl<TValue>)this;
        if (acc is not null)
        {
            acc.Setter(value);
            await BindingHelpers.NotifyAndValidateFieldAsync(ctx, fid);   // commit: changed + touched + revalidate
            await self.InvokeAfterBindAsync(value);                       // helper — runs AfterBind, either shape
        }
        else
        {
            await self.InvokeOnChangeAsync(value);                        // helper — runs OnChange
        }
    }
}
```

Both shapes now work, with the chain generated for you:

```csharp
// Bound — TValue inferred from the expression; validation rides the field:
SegmentedControl.Bind(() => _model.Plan).Options(plans)
    .Validate(p => p == Plan.None ? ["Pick a plan."] : [])

// Controlled — the parent owns the value:
SegmentedControl.Value(_plan).Options(plans).OnChange(p => _plan = p)
```

---

## 3. The helpers `IFormControl<T>` gives you (boilerplate you don't write)

`IFormControl<T>` carries default-method implementations so every control shares the same wiring instead of
re-implementing it. Call them **through the interface** (`((IFormControl<T>)this).X(…)`):

| Member | Replaces |
|---|---|
| `Validator` | `Validate?.Rule` — the single delegate the `EditContext` dispatches, whichever shape it is |
| `RegisterValidator(accessor, ctx)` | `ctx?.RegisterFieldValidator(acc.Field, Validator, () => acc.Getter())` |
| `InvokeAfterBindAsync(value)` | `await AfterBind.Invoke(v)` as a `Task` — one hook, either shape |
| `InvokeOnChangeAsync(value)` | `await OnChange.Invoke(v)` as a `Task` — one handler, either shape |
| `ControlledChangeHandler()` | an `Action<string>` DOM handler that parses the raw value to `T` (`BindingHelpers.TryParseValue`) and calls `InvokeOnChangeAsync` — for controls that wrap a native `<input>`/`<select>` (identity when `T` is string) |

`RegisterValidator` is safe (and required) to call **every render** — passing the collapsed validator each
time also clears a stale rule when the consumer drops `Validate`.

---

## 4. The lower-level binding API

The helpers are built on the public `Rask.Core.Forms` API you can also use directly:

- **`ExpressionAccessor.Parse(Expression)` → `Accessor`** — turns `() => model.Prop` into `Target`,
  `Getter()`/`Setter(value)`, `PropertyName`, `PropertyType`, `Field`. Handles nested chains,
  foreach-captured locals, and indexers (`() => model.Items[i].Name`).
- **`BindingHelpers.ResolveBindingContext(model)` → `EditContext?`** — the surrounding `Form`'s context
  (`null` outside a form / live render).
- **`BindingHelpers.FormatValue(value)` → `string`** — the value→string convention (`<input>` round-trips).
- **`BindingHelpers.TryParseValue(type, raw, out value)`** — the inverse (string→`T`); identity for string,
  enums/`IParsable<T>` via the same parser the bound setter uses.
- **`BindingHelpers.SetCollectionMembership(collection, item, include, comparer?)`** — add/remove an item in
  a bound `ICollection<T>` (what a checkbox group does per toggle).
- **`BindingHelpers.NotifyAndValidateFieldAsync(ctx, field)`** — commit a change: marks the field
  changed + touched and re-validates (no-op when `ctx` is `null`).
- **`Validation.Message.Template(template).For(Bind)`** — render the field's messages inside your control.

---

## 5. Bound vs controlled, and value types

- **Bound** drives the model + `EditContext` (validation, touched-tracking) — the form-integrated shape.
- **Controlled** lets the parent own `Value` and receive `OnChange`; there's no `EditContext`, so no
  validation. Build a *new* value rather than mutating `Value` in place.

`IFormControl<T>` is keyed on one `T`. For a **collection** control, bind a mutable `ICollection<TItem>`
(so toggles can mutate it) and expose the same `T` for `Value`/`OnChange`:
`MultiSelect<TItem> : IFormControl<ICollection<TItem>>`. For a **scalar** control, `T` is the value type
(`SegmentedControl<TValue> : IFormControl<TValue>`).

---

## 6. Stateless helper vs stateful `Component` — host re-render

A control with **no view state** *can* be a plain **static helper method** returning a `Component` (a single
element, or a `[...]` collection of siblings): its handlers are owned by the **host** that declared it, so a
change re-renders the host for free (host-side derived UI just updates). But a static helper isn't a
`Component` subclass, so the generator can't give it a chain and it can't implement `IFormControl<T>`.

A control written as a **`Component`** (required for `IFormControl<T>`, or because it needs view state like an
open/closed dropdown) is its own re-render boundary for *arbitrary* state: a plain toggle re-renders *it*, not
the host. But **two-way binding is not** a boundary — a bound write (`.Bind(() => model.Field)`, in **or**
outside a `Form`) re-renders the component that authored the binding, so host-side derived UI (a sibling whose
class/text is computed from the same model property) updates with no `StateHasChanged`. This holds even when
the bind closed over a loop local (`() => item.Field`): the framework records the control's creating component
as the binding owner (via `RegisterValidator`), so the authoring host re-renders on change. For **controlled**
mode (`Value`/`OnChange`, no `Bind`) the same guarantee comes from `OnChange` being auto-wrapped
(`AutoCallback`) to re-render its owner. Reserve in-control feedback (an embedded `Validation.Message`, chips)
for state the control *itself* owns.

---

## 7. Checklist

1. `sealed partial class MyControl<T> : Component, IFormControl<T>` — declare the nine interface properties + your
   display props.
2. In `Render`: in bound mode `ExpressionAccessor.Parse(Bind)` → `ResolveBindingContext` →
   `((IFormControl<T>)this).RegisterValidator(acc, ctx)`; read the current value from the accessor (bound) or
   `Value` (controlled).
3. In your change handler: bound → `Setter` (or `SetCollectionMembership`) + `NotifyAndValidateFieldAsync` +
   `InvokeAfterBindAsync`; controlled → `InvokeOnChangeAsync`.
4. Surface messages with `Validation.Message.Template(…).For(Bind)` (bound mode).
5. Unit-test both modes (drive the handler, assert the bound model / the emitted `OnChange` value); add an
   E2E if it has a showcase page. Construct via the chain, never `new` (RASK014).

Worked example: `MultiSelect<TItem>` in `Rask.Core`.
