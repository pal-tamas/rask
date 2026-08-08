# Building form controls

Rask ships a few form elements (`Input`/`Select`/`Textarea`), and the optional **`Rask.Bootstrap`** package adds
typed, ready-made controls (`BsMultiSelect`/`BsCheckboxGroup`/`BsRadioGroup`, see [bootstrap.md](bootstrap.md)) —
but the binding system is **public**, so you write exactly the controls your app needs. A custom control gets the same two-way binding, per-field
validation, and controlled mode as the built-ins by implementing one interface: **`IFormControl<T>`**.
The factory generator does the rest.

This is the end-to-end guide. For the wider forms story (binding, `Form<TModel>`, validation layers) see
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
    Carrier<Validate<T>>? Validate { get; set; }
    Carrier<ValidateAsync<T>>? ValidateAsync { get; set; }
    Carrier<Action<T>>? AfterBind { get; set; }
    Carrier<Func<T, Task>>? AfterBindAsync { get; set; }

    // Controlled mode — the parent owns Value and is notified of changes.
    T? Value { get; set; }
    Handler<T>? OnChange { get; set; }
    HandlerAsync<T>? OnChangeAsync { get; set; }
}
```

All six delegate members ride in a **carrier** (`Rask.Core`) — `Carrier<>` for the bound four,
`Handler<T>`/`HandlerAsync<T>` for the change pair. A delegate-typed property *is* invocable, so
`control.Validate(rule)` (or `control.OnChange(h)`) would bind to the property instead of the same-named
builder setter; the carrier makes the member non-invocable. Its implicit conversion keeps ordinary
assignment (`Validate = rule`) and every generated `Validate:` / `OnChange:` factory parameter working
unchanged — read the delegate back through `.Fn` (`Validate?.Fn`, `OnChange?.Fn`).

One trap the conversion brings: it accepts a *null* delegate, so `cond ? new Handler(h) : null` hands back a
non-null carrier wrapping null — an unset handler that no longer reads back as unset. Cast the unset branch
(`: (Handler?)null`), or build it with `Handler.From(h)`, which maps null to unset. Every generated
assignment already does.

The rule is not limited to the interface's members, or to names beginning with `On`: **any** delegate-typed
property your control declares needs a carrier if you want a builder setter for it. `OptionLabel`,
`RowClass`, a `Filter` predicate — all of them are invocable as declared, so the setter of the same name can
never be reached. The generator reports the ones it finds as **RASK039** and names the carrier to use.

You declare those nine properties (plus your own display props), implement `Render`, and the generator
emits **two factories**:

- a **controlled** factory — `MyControl<T>(Value: …, OnChange: …, …display…)`,
- a **bound** factory — `MyControl(() => model.Field, …)` with the validator fanned into none/sync/async
  overloads (so `Validate:` accepts a sync `Validate<T>` *or* an async `ValidateAsync<T>` with no cast).

The bound-mode members are excluded from the controlled factory automatically — no `[SkipFactory]`.

---

## 2. A complete example — `SegmentedControl<TValue>`

A single-select rendered as a row of buttons (think iOS segmented control). It binds one `TValue` chosen
from `Options`, works bound or controlled, and supports per-field validation.

```csharp
using System.Linq.Expressions;
using Rask.Core.Forms;

namespace MyApp.Controls;

public sealed class SegmentedControl<TValue> : Component, IFormControl<TValue>
{
    public required IEnumerable<TValue> Options { get; set; }
    // A carrier, not a raw `Func<…>`: a delegate-typed property IS invocable, so `.OptionLabel(fn)`
    // would bind to the property and never reach the same-named builder setter. Same reason the four
    // bound members below use one. Assignment and every generated `OptionLabel:` argument are
    // unchanged; reading the delegate back is `.Fn`.
    public Carrier<Func<TValue, Component>>? OptionLabel { get; set; }
    public string? Class { get; set; }

    // IFormControl<TValue> — controlled mode.
    public TValue? Value { get; set; }
    public Handler<TValue>? OnChange { get; set; }
    public HandlerAsync<TValue>? OnChangeAsync { get; set; }

    // IFormControl<TValue> — bound mode.
    public Expression<Func<TValue>>? Bind { get; set; }
    public Carrier<Validate<TValue>>? Validate { get; set; }
    public Carrier<ValidateAsync<TValue>>? ValidateAsync { get; set; }
    public Carrier<Action<TValue>>? AfterBind { get; set; }
    public Carrier<Func<TValue, Task>>? AfterBindAsync { get; set; }

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
            buttons.Add(Button(
                Type: "button",
                Class: active ? "btn btn-primary" : "btn btn-outline-primary",
                OnClickAsync: () => SelectAsync(acc, ctx, fid, captured),
                Key: i++)[OptionLabel?.Fn is { } label ? label(option) : option?.ToString() ?? ""]);
        }

        var children = new List<Component> { Div(Class: "btn-group")[buttons] };
        if (Bind is not null)
        {
            children.Add(ValidationMessage(Bind, msgs => Div(Class: "invalid-feedback d-block")[msgs[0]]));
        }

        return Div(Class: Class ?? "segmented")[children];
    }

    private async Task SelectAsync(
        ExpressionAccessor.Accessor? acc, EditContext? ctx, FieldIdentifier fid, TValue value)
    {
        var self = (IFormControl<TValue>)this;
        if (acc is not null)
        {
            acc.Setter(value);
            await BindingHelpers.NotifyAndValidateFieldAsync(ctx, fid);   // commit: changed + touched + revalidate
            await self.InvokeAfterBindAsync(value);                       // helper — runs AfterBind/AfterBindAsync
        }
        else
        {
            await self.InvokeOnChangeAsync(value);                        // helper — runs OnChange/OnChangeAsync
        }
    }
}
```

Both shapes now work, with the factories generated for you:

```csharp
// Bound — TValue inferred from the expression; validation rides the field:
SegmentedControl(() => _model.Plan, Options: plans,
    Validate: p => p == Plan.None ? ["Pick a plan."] : [])

// Controlled — the parent owns the value:
SegmentedControl(Options: plans, Value: _plan, OnChange: p => _plan = p)
```

---

## 3. The helpers `IFormControl<T>` gives you (boilerplate you don't write)

`IFormControl<T>` carries default-method implementations so every control shares the same wiring instead of
re-implementing it. Call them **through the interface** (`((IFormControl<T>)this).X(…)`):

| Member | Replaces |
|---|---|
| `Validator` | `(Delegate?)Validate?.Fn ?? ValidateAsync?.Fn` — the single delegate the `EditContext` dispatches |
| `RegisterValidator(accessor, ctx)` | `ctx?.RegisterFieldValidator(acc.Field, Validator, () => acc.Getter())` |
| `InvokeAfterBindAsync(value)` | `AfterBind?.Fn?.Invoke(v); if (AfterBindAsync?.Fn is { } h) await h(v);` |
| `InvokeOnChangeAsync(value)` | `OnChange?.Invoke(v); if (OnChangeAsync?.Fn is { } f) await f(v);` |
| `ControlledChangeHandler()` | a `Callback<string>` DOM handler that parses the raw value to `T` (`BindingHelpers.TryParseValue`) and calls `InvokeOnChangeAsync` — for controls that wrap a native `<input>`/`<select>` (identity when `T` is string) |

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
- **`ValidationMessage(Bind, template)`** — render the field's messages inside your control.

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

A control with **no view state** *can* be a plain **static factory method** returning a `Component` (a single
element, or a `[...]` collection of siblings): its handlers are owned by the **host** that declared it, so a
change re-renders the host for free (host-side derived UI just updates). But a static helper isn't a
`Component` subclass, so the generator can't synthesize a factory for it and it can't implement `IFormControl<T>`.

A control written as a **`Component`** (required for `IFormControl<T>`, or because it needs view state like an
open/closed dropdown) is its own re-render boundary for *arbitrary* state: a plain toggle re-renders *it*, not
the host. But **two-way binding is not** a boundary — a bound write (`Bind: () => model.Field`, in **or**
outside a `Form`) re-renders the component that authored the binding, so host-side derived UI (a sibling whose
class/text is computed from the same model property) updates with no `StateHasChanged`. This holds even when
the bind closed over a loop local (`() => item.Field`): the framework records the control's creating component
as the binding owner (via `RegisterValidator`), so the authoring host re-renders on change. For **controlled**
mode (`Value`/`OnChange`, no `Bind`) the same guarantee comes from `OnChange` being auto-wrapped
(`AutoCallback`) to re-render its owner. Reserve in-control feedback (an embedded `ValidationMessage`, chips)
for state the control *itself* owns.

---

## 7. Checklist

1. `sealed class MyControl<T> : Component, IFormControl<T>` — declare the nine interface properties + your
   display props.
2. In `Render`: in bound mode `ExpressionAccessor.Parse(Bind)` → `ResolveBindingContext` →
   `((IFormControl<T>)this).RegisterValidator(acc, ctx)`; read the current value from the accessor (bound) or
   `Value` (controlled).
3. In your change handler: bound → `Setter` (or `SetCollectionMembership`) + `NotifyAndValidateFieldAsync` +
   `InvokeAfterBindAsync`; controlled → `InvokeOnChangeAsync`.
4. Surface messages with `ValidationMessage(Bind, …)` (bound mode).
5. Unit-test both modes (drive the handler, assert the bound model / the emitted `OnChange` value); add an
   E2E if it has a showcase page. Construct via the generated factory, never `new` (RASK014).

Worked examples: the `BsRadioGroup`/`BsCheckboxGroup`/`BsMultiSelect` controls in the **Rask.Bootstrap**
package (`src/Rask.Bootstrap/Bs{RadioGroup,CheckboxGroup,MultiSelect}.cs`).
