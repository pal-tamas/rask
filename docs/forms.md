# Forms & validation

Rask binds inputs two-way with a strongly-typed `Bind` expression, routes submit through
validators you opt into, and tracks per-field state (touched / modified / messages / in-flight
validation) on an `EditContext`. The same component code runs server-rendered or on WASM.

This guide builds up in layers: binding → forms → inline validation → DataAnnotations →
FluentValidation → async → nested models → radio/checkbox groups.

For the analyzer IDs referenced here (`RASK001`, `RASK022`, …) see [diagnostics.md](diagnostics.md).

## On this page

- [Validation](forms-validation.md) — inline, DataAnnotations, FluentValidation, async.
- [Nested models & control groups](forms-advanced.md) — complex models, radio/checkbox groups, custom controls.

---

## 1. Two-way binding

The low-level path wires `Value` and an event handler yourself:

```csharp
Input.Value(_typed).Type(InputType.Text).OnInput(v => _typed = v)
P[$"Echo: {_typed}"]
```

<!-- demo:binding-manual -->

The ergonomic path is a `Bind` expression — one call replaces `Value` + `OnInput` + parsing:

```csharp
Input.Bind(() => _model.Name).Placeholder("Your name")
P[$"Hello, {_model.Name}!"]
```

<!-- demo:binding-typed -->

`Bind` is an `Expression<Func<TProp>>` (`Input.Bound<TProp>`). The chain step reads the expression and
derives everything from the bound property:

- **Input name** ← the property name (`name="Name"`). Override with `Name:`.
- **Input type** ← the property's CLR type (`BindingHelpers.DefaultInputType`):
  `bool → checkbox`, every numeric primitive `→ number`, `DateOnly → date`,
  `DateTime`/`DateTimeOffset` `→ datetime-local`, `TimeOnly`/`TimeSpan` `→ time`, everything else
  `→ text`. Override with `Type:` — an `InputType` enum value. The full set is
  `Text`, `Search`, `Tel`, `Url`, `Email`, `Password`, `Number`, `Checkbox`, `Radio`, `File`, `Range`,
  `Color`, `Date`, `DatetimeLocal` (renders `datetime-local`), `Time`, `Week`, `Month`, `Hidden`,
  `Button`, `Submit`, `Reset`, `Image`. The *string-only* family (`Text`/`Search`/`Tel`/`Url`/`Email`/
  `Password`) only makes sense on an `Input<string>`; setting one on a non-string bound input is
  [RASK025](diagnostics.md#rask025).
- **Update timing** — `string` fields update on every keystroke (`OnInput`); every other type
  updates on `OnChange` (blur). `Textarea(() => …)` always streams on `OnInput`.

### The two modes are exclusive

A control's value comes from exactly one place, and the step you open the chain with says which:

| Opened with | Mode | Then adds | Does **not** offer |
| --- | --- | --- | --- |
| `.Bind(() => model.Field)` | bound | `Validate` / `ValidateAsync`, `AfterBind` / `AfterBindAsync` | `Checked`, `OnInput`, `OnChange` |
| `.Value(v)` or `.Of<T>()` | controlled | `Checked`, `OnInput` / `OnInputAsync`, `OnChange` / `OnChangeAsync` | `Validate`, `AfterBind` |

`Bind` and `Value` are the *openings* themselves, not steps you take later — taking one is what rules
the other out, so neither appears again on the chain. `Of<T>()` opens a controlled chain for a control
you are giving no value at all; if you want to supply one, that is `.Value(v)`.

Everything else — `Placeholder`, `Type`, `Required`, `Min`/`Max`, `OnFiles`, the whole `Class`/`Id`/
`Aria` element surface — belongs to neither and is reachable from both.

This is enforced by the type, not by a convention: the chain is a
`Build<TControl, Bound>` or a `Build<TControl, Controlled>`, and a step from the other mode is not
offered in completion and does not compile.

```csharp
Input.Bind(() => _model.Name).OnInput(v => _log = v)   // ✗ no such step on a bound chain
Input.Value(_typed).AfterBind(v => Save(v))            // ✗ no such step on a controlled chain
```

The reason is that bound mode already owns those: it derives the rendered value (and a checkbox's
`checked`) from the model and installs its own `oninput`/`onchange` write-back. Setting `OnInput`
alongside `Bind` used to compile and then be dropped at render time, silently. Want a side effect on
each bound write? That is what `AfterBind` is for.

The generated factories carry the same split — `Input(() => m.Name, OnInput: …)` has no such
parameter — so neither surface can express a mode it will not honour.

Beyond the constraint/affordance attributes shared with plain HTML (`Min`/`Max`/`Step`/`Pattern`/
`MaxLength`/`MinLength`/`Multiple`/`Accept`/`List`/`Autocomplete`/`Autofocus`), the core `Input` also
carries the mobile & accessibility hints `InputMode` (on-screen keyboard), `EnterKeyHint` (action-key
label), `Spellcheck` (the enumerated `spellcheck="true|false"`), `Capture` (camera/mic for a file
input), and `Dirname`. A control of your own forwards them the same way (see
[building-form-controls.md](building-form-controls.md)).

> **Fractional numbers get `step="any"` automatically.** A `decimal`/`double`/`float`/`Half` binding
> renders `<input type="number" step="any">`. Without it HTML's default is `step="1"`, so the browser's own
> constraint validation rejects `42.50` and **refuses to fire submit** — silently, with nothing thrown and
> no validation message, which reads as the form being broken. Integral types keep the implicit whole-number
> constraint. An explicit `Step:` always wins, and is worth setting for money (`Step: "0.01"` makes the
> spinner step by cents).

### File inputs

`InputType.File` turns an `<input>` into a file picker. Instead of binding a value, hand it an
`OnFiles` (or `OnFilesAsync`) callback that receives the selected `RaskFile`s (`Name`/`Size`/
`ContentType`/`OpenReadStream()`), and constrain the picker with `Accept`, `Multiple`, and `Capture`:

```csharp
Input<string>().Type(InputType.File).Accept("image/*").Multiple(true)
     .FilesAsync(async files => { foreach (var f in files) await Save(f); })
```

Uploading the bytes (streaming to a server endpoint, size limits, progress) is covered end-to-end in
[http-and-files.md](http-and-files.md).

```csharp
Input.Bind(() => _model.Subscribe)   // bool     → checkbox
Input.Bind(() => _model.Age)         // int      → number
Input.Bind(() => _model.StartDate)   // DateOnly → date
Select.Bind(() => _model.Favorite)[Option("Red")["Red"], Option("Blue")["Blue"]]
Textarea.Bind(() => _model.Notes).Rows(3)
```

<!-- demo:binding-multi -->

<!-- demo:binding-textarea -->

### Empty value handling

When the user clears an input, `BindingHelpers.TrySetTyped` decides what the empty string maps to:

| Property kind | Empty input becomes |
|---|---|
| `Nullable<T>` value type (`int?`, `DateOnly?`, …) | `null` |
| NRT-nullable reference type (`string?`) | `null` (detected via `NullabilityInfoContext`) |
| Non-nullable value type (`int`, `DateOnly`, enum) | `default(T)` — so a number/date/enum input is clearable |
| Non-nullable `string` | `""` (verbatim) |

A value that fails to parse (`"not-a-number"` into an `int`) leaves the model unchanged.

Every BCL [`IParsable<T>`](https://learn.microsoft.com/dotnet/api/system.iparsable-1) type (numbers,
`Guid`, `DateOnly`/`DateTime`/`TimeOnly`, `bool`, …) binds with no setup, and so does a **custom**
`IParsable<T>` value type under the default interpreter build. For a full [WASM AOT](aot.md) build,
register your custom form-field types once at startup with `RaskBinding.RegisterParsable<Money>()` —
custom route/query param types are registered automatically by the generator.

<!-- demo:binding-nullable -->

<!-- demo:binding-clear-default -->

### Binding lifecycle

Each change handler runs in order: write the value, `NotifyFieldChanged`, run `AfterBind`/
`AfterBindAsync` (if supplied, only when a write actually happened), `NotifyFieldTouched` (on
change/blur), then re-validate the field. `string` inputs stay quiet until the field is touched,
then re-validate on every keystroke so a correction clears the message without a blur.

`AfterBind` / `AfterBindAsync` fire **after** the new value is written and before validators run —
handy for dependent fields (pick a country, repopulate the city dropdown in the same render):

```csharp
Select.Bind(() => _model.Country)
    .AfterBind(c => { _cities = Cities[c]; _model.City = _cities[0]; })[ /* options */ ]
Select.Bind(() => _model.City)[_cities.Select(c => Option.Value(c)[c])]
```

<!-- demo:binding-afterbind -->

`AfterBindAsync` awaits before the post-handler render, so a dependent async lookup (repopulate a
dropdown from an API) surfaces its loading state on its own — no manual `StateHasChanged()`:

<!-- demo:binding-afterbind-async -->

### What a bind costs, and the one case worth changing

`Bind(() => …)` takes an `Expression<Func<T>>`, and **the C# compiler builds that tree at the call
site on every render**. Building it resolves a member token on the bound property's *declaring type*,
and that cost scales with how many members the type has:

| what you bind | B/render |
| --- | --- |
| `Input.Value(…)` — controlled, no bind at all | 1216 |
| `Input.Bind(() => Model.Name)` — a plain model | 3041 |
| `Input.Bind(() => Draft)` — a property on the **component** | 5011 |

The third row is the surprise, and it is invisible at the call site: the two spellings look equally
cheap. A component is a markup host, so the generator injects the chain entries into it — around 430
members — and binding a property *declared there* pays for resolving a token on a type that large.
Binding the same value on a plain model does not.

**For nearly every app this does not matter.** A form re-renders on interaction, not in a loop; a few
kilobytes per render is well under the noise. Reach for the fix below only when a bound control is in
something genuinely hot — a virtualized grid, a control that re-renders on every keystroke of a large
document.

**The fix is at the call site: hoist the expression into a field**, so it is built once instead of
per render.

```csharp
using System.Linq.Expressions;

public sealed partial class Editor : Component
{
    private readonly Model _model = new();

    // Built once, in the constructor, rather than on every render.
    private readonly Expression<Func<string>> _name;

    public Editor() => _name = () => _model.Name;

    protected override Component Render() => Input.Bind(_name);
}
```

The two rows converge once the tree stops being rebuilt: **2721 B/render** hoisted against a plain
model, **2753 B** hoisted against a property on the component — so hoisting does not merely help the
expensive shape, it erases the difference. What remains is the binding machinery (`FieldIdentifier`,
validator registration, owner tracking), which is shared with the plain-model case.

Every number on this page is pinned by `BuilderEntryAllocationPinTests`, so none of them can drift
without a test going red.

The other move is simply **to bind a plain model rather than a property on the component**
(`() => _model.Name`, not `() => Draft`), which is what most code does already and costs 3041 B rather
than 5011 B without any ceremony.

> Why not fix this in the framework? Measured and ruled out in
> [#803](https://github.com/pal-tamas/rask/issues/803): a Roslyn interceptor cannot help — an
> interceptor must keep the intercepted method's signature (CS9144), and the lambda is converted to an
> expression tree when the call is *bound*, before interception applies. Adding a `Func<T>` overload
> does not help either, because C# prefers the `Expression` one. What remains is making the injected
> chain entries inheritable instead, which is a change to how every markup host is generated.

---

## 2. `Form<TModel>` and the `EditContext`

`Form<TModel>(model, …)` wraps the inputs and owns an `EditContext` — the per-field state store
plus the validator pipeline. Bound inputs inside the form discover that context automatically.

```csharp
Form<SignupModel>(_model, OnValidSubmit: m => Console.WriteLine(m.Username))[
    Input.Bind(() => _model.Username),
    Button.Type("submit")["Sign up"]
]
```

Submit runs the full validator pipeline (`ValidateAsync`), marks every registered field touched,
then routes:

- valid → `OnValidSubmit` (or, if unset, `OnSubmit` / `OnSubmitAsync` with the raw `FormData`),
- invalid → `OnInvalidSubmit`.

`OnValidSubmit` / `OnInvalidSubmit` accept `Action<TModel>` or `Func<TModel, Task>` — the generic
overload narrows the delegate so you pass a bare lambda with no cast.

### Auto-created vs explicit `Context`

By default the form creates (and caches per model reference) its own `EditContext` — it persists
across renders of the same model, so field state survives re-renders. Pass `Context:` to own the
instance yourself when you need to drive validation imperatively, register an
`IAsyncFieldValidator`, or tune `ValidatingStickyMs`:

```csharp
_ctx = new EditContext(_model);
_ctx.AddValidator(new SlowTitleValidator());

Form<TaskModel>(_model, m => _submission = "Saved", Context: _ctx)[
    Input.Bind(() => _model.Title),
    Button.Type("button").OnClickAsync(() => _ctx.ValidateAsync().AsTask())["Validate now"],
    Button.Type("submit").Disabled(_ctx.IsValidatingAny)["Save"]
]
```

`Form` requires either `Model` or `Context`.

### Rendering messages

Two headless components read the context — both take a required `Template:` so you own the markup,
and both render nothing when there's nothing to show:

```csharp
ValidationMessage.For(() => _model.Email).Template(errs => Div.Class("field-error")[errs[0]])

ValidationSummary.Template(entries => Ul[entries.Select(e => Li[Strong[e.Field], ": ", e.Message])])
```

`ValidationMessage.For` keys a single field; `ValidationSummary` lists every `ValidationEntry`
(`Field` + `Message`), with form-level messages carrying an empty `Field`.

<!-- demo:validation-summary -->

### Controls at a glance

Every input works in two shapes — **controlled** (`Value` + `OnChange`, the parent owns the value) and
**bound** (`Bind: () => model.X`, two-way). A derived readout rendered *outside* the control updates
live either way. The matrix below covers text, textarea, select, and the component
controls (`BsRadioGroup` / `BsCheckboxGroup` / `BsMultiSelect`).

<!-- demo:form-controls-input -->

<!-- demo:form-controls-textarea -->

<!-- demo:form-controls-select -->

**Floating labels.** The reusable `Floating*` wrappers (input/select/textarea) render a
floating-label field with the label derived from the bound property, and surface validation via
`.field-error`:

<!-- demo:floating-labels -->

### Accessible validation

A control of your own (see [building form controls](building-form-controls.md)),
`BsRadioGroup`, `BsCheckboxGroup`) expose validation to assistive tech automatically — no extra props.
When a bound field has messages, the control renders `aria-invalid="true"`, an `aria-describedby` that
points at the error message's `id` (and the help-text `id` when `HelpText:` is set), and the
`.invalid-feedback` as a `role="alert"` live region so screen readers announce the error the moment it
appears, associated with the field rather than detached from it. Valid fields with `HelpText:` still get
`aria-describedby` to the help text.

The combobox controls (`BsSelect`'s custom dropdown, `BsMultiSelect`) are a `<div role="combobox">`,
which is not a labelable element — so their visible label is tied to them with `aria-labelledby` (not a
void `<label for>`), alongside the `aria-haspopup`/`aria-expanded`/`aria-controls` popup contract. Give
`BsRadioGroup`/`BsCheckboxGroup` a `Label:` and the options are wrapped in a `<fieldset>` named by a
`<legend>` — the correct grouping semantics and accessible name for a set of related radios/checkboxes.

If you build your own control from the core `Input`/`ValidationMessage` primitives (§9), mirror the same
three attributes so the field stays accessible: `aria-invalid` on the control, `aria-describedby` from
the control to the message `id`, and `role="alert"` on the message container. See
[accessibility.md](accessibility.md#form-validation).
