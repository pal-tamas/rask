# Forms & validation

Rask binds inputs two-way with a strongly-typed `Bind` expression, routes submit through
validators you opt into, and tracks per-field state (touched / modified / messages / in-flight
validation) on an `EditContext`. The same component code runs server-rendered or on WASM.

This guide builds up in layers: binding → forms → inline validation → DataAnnotations →
FluentValidation → async → nested models → radio/checkbox groups.

For the analyzer IDs referenced here (`RASK001`, `RASK022`, …) see [diagnostics.md](diagnostics.md).

---

## 1. Two-way binding

The low-level path wires `Value` and an event handler yourself:

```csharp
Input(Type: "text", Value: _typed, OnInput: v => _typed = v)
P()[$"Echo: {_typed}"]
```

The ergonomic path is a `Bind` expression — one call replaces `Value` + `OnInput` + parsing:

```csharp
Input(Bind: () => _model.Name, Placeholder: "Your name")
P()[$"Hello, {_model.Name}!"]
```

`Bind` is an `Expression<Func<TProp>>` (`Input.Bound<TProp>`). The factory reads the expression and
derives everything from the bound property:

- **Input name** ← the property name (`name="Name"`). Override with `Name:`.
- **Input type** ← the property's CLR type (`BindingHelpers.DefaultInputType`):
  `bool → checkbox`, every numeric primitive `→ number`, `DateOnly → date`,
  `DateTime`/`DateTimeOffset` `→ datetime-local`, `TimeOnly`/`TimeSpan` `→ time`, everything else
  `→ text`. Override with `Type:`.
- **Update timing** — `string` fields update on every keystroke (`OnInput`); every other type
  updates on `OnChange` (blur). `Textarea(Bind: …)` always streams on `OnInput`.

```csharp
Input(Bind: () => _model.Subscribe)   // bool     → checkbox
Input(Bind: () => _model.Age)         // int      → number
Input(Bind: () => _model.StartDate)   // DateOnly → date
Select(Bind: () => _model.Favorite)[Option("Red")["Red"], Option("Blue")["Blue"]]
Textarea(Bind: () => _model.Notes, Rows: 3)
```

### Empty value handling

When the user clears an input, `BindingHelpers.TrySetTyped` decides what the empty string maps to:

| Property kind | Empty input becomes |
|---|---|
| `Nullable<T>` value type (`int?`, `DateOnly?`, …) | `null` |
| NRT-nullable reference type (`string?`) | `null` (detected via `NullabilityInfoContext`) |
| Non-nullable value type (`int`, `DateOnly`, enum) | `default(T)` — so a number/date/enum input is clearable |
| Non-nullable `string` | `""` (verbatim) |

A value that fails to parse (`"not-a-number"` into an `int`) leaves the model unchanged.

### Binding lifecycle

Each change handler runs in order: write the value, `NotifyFieldChanged`, run `AfterBind`/
`AfterBindAsync` (if supplied, only when a write actually happened), `NotifyFieldTouched` (on
change/blur), then re-validate the field. `string` inputs stay quiet until the field is touched,
then re-validate on every keystroke so a correction clears the message without a blur.

`AfterBind` / `AfterBindAsync` fire **after** the new value is written and before validators run —
handy for dependent fields (pick a country, repopulate the city dropdown in the same render):

```csharp
Select(Bind: () => _model.Country,
       AfterBind: c => { _cities = Cities[c]; _model.City = _cities[0]; })[ /* options */ ]
Select(Bind: () => _model.City)[_cities.Select(c => Option(Value: c)[c])]
```

---

## 2. `Form<TModel>` and the `EditContext`

`Form<TModel>(model, …)` wraps the inputs and owns an `EditContext` — the per-field state store
plus the validator pipeline. Bound inputs inside the form discover that context automatically.

```csharp
Form<SignupModel>(_model, OnValidSubmit: m => Console.WriteLine(m.Username))[
    Input(Bind: () => _model.Username),
    Button(Type: "submit")["Sign up"]
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
    Input(Bind: () => _model.Title),
    Button(Type: "button", OnClickAsync: () => _ctx.ValidateAsync().AsTask())["Validate now"],
    Button(Type: "submit", Disabled: _ctx.IsValidatingAny)["Save"]
]
```

`Form` requires either `Model` or `Context`.

### Rendering messages

Two headless components read the context — both take a required `Template:` so you own the markup,
and both render nothing when there's nothing to show:

```csharp
ValidationMessage(For: () => _model.Email,
    Template: errs => Div(Class: "field-error")[errs[0]])

ValidationSummary(
    Template: entries => Ul()[entries.Select(e => Li()[Strong()[e.Field], ": ", e.Message])])
```

`ValidationMessage.For` keys a single field; `ValidationSummary` lists every `ValidationEntry`
(`Field` + `Message`), with form-level messages carrying an empty `Field`.

---

## 3. Inline validation (no extra package)

The lightest layer ships in `Rask.Core`. Pass a `Validate:` lambda — per-field or per-form. Both
accept a sync `Func<…, IEnumerable<string>>` or an async
`Func<…, CancellationToken, ValueTask<IEnumerable<string>>>`; overload resolution picks by arity, no
cast. An empty sequence means valid.

```csharp
Form<LoginModel>(_model,
    OnValidSubmit: m => _submission = "Welcome",
    Validate: m => m.Password == m.Confirm ? [] : ["Passwords do not match."])[   // cross-field, at submit
    Input(Bind: () => _model.Email,
          Validate: v => v.Contains('@') ? [] : ["Email looks wrong."]),          // per-field, per-keystroke
    ValidationMessage(For: () => _model.Email, Template: errs => Div(Class: "err")[errs[0]]),
    ValidationSummary(Template: SummaryAlert),
    Button(Type: "submit")["Sign in"]
]
```

Per-field `Validate:` produces field-scoped messages and runs on each keystroke after the field is
touched. Form-level `Validate:` runs at submit and attaches messages to the form-level slot
(`FieldIdentifier(model, "")`) — they surface in `ValidationSummary`, never against a specific input.

---

## 4. DataAnnotations

Add the `Rask.Validation.DataAnnotations` package and drop `DataAnnotationsValidator()` inside the
form. It's a real (headless) component that registers an `IFieldValidator` on the form's
`EditContext` via `EditContextScope.Current?.AddValidator(...)` — one declaration covers the whole
reachable model graph. The package adds a global static using, so the factory is in scope.

```csharp
public sealed class SignupModel
{
    [Required, StringLength(20, MinimumLength = 3)] public string Username { get; set; } = "";
    [Required, EmailAddress]                        public string Email    { get; set; } = "";
}

Form<SignupModel>(_model, OnValidSubmit: m => Console.WriteLine(m.Username))[
    DataAnnotationsValidator(),
    Input(Bind: () => _model.Username),
    ValidationMessage(For: () => _model.Username, Template: errs => Div(Class: "err")[errs[0]]),
    Input(Bind: () => _model.Email),
    ValidationMessage(For: () => _model.Email, Template: errs => Div(Class: "err")[errs[0]]),
    Button(Type: "submit")["Register"]
]
```

Supports `[Required]`, `[EmailAddress]`, `[Range]`, `[StringLength]`, `[RegularExpression]`, custom
`ValidationAttribute` subclasses, and `IValidatableObject`. Unlike the BCL's
`Validator.TryValidateObject`, Rask invokes `IValidatableObject.Validate` even when attribute errors
exist — so attribute and object-level errors surface together (ASP.NET Core MVC parity). The
`ValidationContext` is built with the render-scoped `IServiceProvider`, so custom attributes can call
`ctx.GetService<T>()`.

A `ValidationResult` with empty `MemberNames` lands on the form-level slot (`ValidationSummary`); a
populated one tags the named field.

---

## 5. FluentValidation

Add the `Rask.Validation.FluentValidation` package and drop
`FluentValidationValidator(new MyValidator())` inside the form. It wraps any
`FluentValidation.IValidator` into an `IAsyncFieldValidator` (so async `MustAsync` rules work too).

```csharp
public sealed class OrderValidator : AbstractValidator<OrderModel>
{
    public OrderValidator()
    {
        RuleFor(x => x.Product).NotEmpty();
        RuleFor(x => x.Quantity).GreaterThanOrEqualTo(1).WithMessage("Quantity must be at least 1.");
    }
}

Form<OrderModel>(_model, m => _submission = "Ordered")[
    FluentValidationValidator(new OrderValidator()),
    Input(Bind: () => _model.Product),
    ValidationMessage(For: () => _model.Product, Template: errs => Div(Class: "err")[errs[0]]),
    Input(Bind: () => _model.Quantity),
    ValidationMessage(For: () => _model.Quantity, Template: errs => Div(Class: "err")[errs[0]]),
    Button(Type: "submit")["Order"]
]
```

Per-keystroke validation on a root-model field scopes FluentValidation to that single property
(`MemberNameValidatorSelector`, fast path); submit runs every rule. FluentValidation's own
`Cascade(CascadeMode.Stop)` mirrors Rask's first-error-wins gating.

---

## 6. Async validators and the validating indicator

Three ways to validate asynchronously:

1. **Inline async `Validate:`** — return a `ValueTask<IEnumerable<string>>`. The `CancellationToken`
   cancels the in-flight check on the next keystroke (latest-wins).
2. **`IAsyncFieldValidator`** — reach for this when the rule needs DI (an `HttpClient`, a
   repository) or you want to reuse it across forms. Add it to an `EditContext` you own:

   ```csharp
   public sealed class UniqueUsernameValidator : IAsyncFieldValidator
   {
       public async ValueTask ValidateFieldAsync(EditContext ctx, FieldIdentifier field, CancellationToken ct)
       {
           if (ctx.Model is SignupModel m && field.FieldName == nameof(SignupModel.Username))
           {
               await Task.Delay(400, ct);            // pretend it's an API call
               if (await IsTakenAsync(m.Username))
                   ctx.AddValidationMessage(field, "Already taken.");
           }
       }
       public ValueTask ValidateAsync(EditContext c, CancellationToken ct) => default;
   }

   _ctx = new EditContext(_model);
   _ctx.AddValidator(new UniqueUsernameValidator());
   // Form<…>(_model, Context: _ctx)[ … ]
   ```
3. **FluentValidation `MustAsync`** — async rules ride the same `FluentValidationValidator` wrapper.

Each `await` in a handler triggers a re-render, so a `ValidatingIndicator` can surface while a
check is in flight:

```csharp
ValidatingIndicator(For: () => _model.Username,
    Template: () => Span(Class: "spinner")["Checking…"])
```

### `IsValidating` vs `ShouldShowValidatingIndicator`

- `EditContext.IsValidating(field)` / `IsValidatingAny` — the exact "a validator is in flight right
  now" answer. Use it for control flow (e.g. `Disabled: _ctx.IsValidatingAny` on a submit button).
- `ShouldShowValidatingIndicator(field)` — `IsValidating` extended with a short **sticky tail**
  (`EditContext.ValidatingStickyMs`, default 200ms). A sub-second check still reads as "showing" for
  the sticky window so the indicator has a footprint screen-readers and Playwright can observe. This
  is what `ValidatingIndicator` renders against. The sticky dismissal is a single timer-driven
  re-render at window expiry. Set `ValidatingStickyMs = 0` on a context you own to opt out; it does
  not delay submit or validator completion.

### First-error-wins

The pipeline runs inline → form-level inline → sync `IFieldValidator` → async
`IAsyncFieldValidator`. Once any stage flags a field, later stages stay quiet on that **same** field
— so fixing one error reveals the next rule's message. A validator that throws mid-check surfaces a
generic `"Validation could not be completed."` rather than killing the submit pipeline.

---

## 7. Nested / complex models

`Bind` and validation extend transparently through sub-objects and collections. A single
`DataAnnotationsValidator()` or `FluentValidationValidator(...)` at the top of the form covers the
whole reachable graph — no per-level opt-in. `FieldIdentifier` is **reference-based** (keyed off the
owner sub-instance, not a dotted path from the root), so removing or replacing a row drops its error
state with it.

```csharp
public sealed class CheckoutModel
{
    [Required] public string Name { get; set; } = "";
    public AddressModel Address { get; set; } = new();
    public List<LineItem> Items { get; set; } = new();
}
public sealed class AddressModel
{
    [Required] public string Street { get; set; } = "";
    [Required, RegularExpression("^[A-Z]{2}$")] public string Country { get; set; } = "";
}
```

**Sub-object binding** uses the same `Bind: () => …` shape:

```csharp
Input(Bind: () => _model.Address.Street),
ValidationMessage(For: () => _model.Address.Street, Template: errs => Div(Class: "err")[errs[0]]),
```

**Collection binding — `foreach` + per-item capture** (the canonical pattern). Each iteration closes
over a distinct `item`, so each row's lambda targets its own instance:

```csharp
foreach (var item in _model.Items)
{
    rows.Add(Tr()[
        Td()[Input(Bind: () => item.Description)],
        Td()[Input(Bind: () => item.Quantity)],
        Td()[Button(Type: "button", OnClick: () => _model.Items.Remove(item))["×"]]
    ]);
}
```

**Collection binding — indexer style** when you need the row number, or for records that get
replaced rather than mutated (`() => model.Items[i].Name` re-resolves the slot every render). Watch
the classic `for` closure trap — copy the index into a per-iteration local:

```csharp
for (var idx = 0; idx < _model.Items.Count; idx++)
{
    var i = idx;                                  // per-iteration capture, NOT idx
    rows.Add(Tr()[
        Td()[$"#{i + 1}"],
        Td()[Input(Bind: () => _model.Items[i].Description)],
        Td()[Input(Bind: () => _model.Items[i].Quantity)]
    ]);
}
```

`foreach` has no closure trap. Records with init-only properties can't be auto-bound through the
setter — declare them `{ get; set; }`, or use the indexer pattern with a manual handler that replaces
the slot (`_model.Items[i] = _model.Items[i] with { Field = newValue }`).

**FluentValidation nesting** uses `SetValidator(...)` and `RuleForEach(...).SetValidator(...)`; Rask
routes the dotted `error.PropertyName` (`Address.Street`, `Lines[0].Quantity`) back to the runtime
sub-instance so `ValidationMessage(For: () => _model.Address.Street, …)` reads the right slot.

> **Trimming.** Validating a nested graph reflects over every reachable model type. Whatever
> preserves the root model's public properties (`[DynamicallyAccessedMembers]`, a routed page, or a
> trimmer descriptor) must extend to every nested type.

See `samples/Rask.Example.Shared/Features/NestedForms/NestedFormPage.cs` for all four patterns side by side.

---

## 8. Radio & checkbox groups (example components)

`RadioGroup<TValue>` binds one value from a set of options; `CheckboxGroup<TItem>` binds an
`ICollection<TItem>`. They are **example components that ship in the samples**
(`samples/Rask.Example.Shared/Shared/`), not framework primitives — small, copyable controls built on the
public binding API of §9, structured exactly like `MultiSelect<TItem>` with **bound** and **controlled**
modes (so the generator emits their factories):

```csharp
// Bound — two-way binds the model, with an optional per-field Validate rule.
Form(_prefs)[
    RadioGroup(() => _prefs.Plan,                       // single value
        new[] { Plan.Free, Plan.Pro, Plan.Team },
        ItemClass: "form-check-inline"),

    CheckboxGroup<string>(() => _prefs.Interests,       // a collection
        new[] { "Web", "Mobile", "AI", "Games" },
        Validate: tags => tags.Count >= 1 ? [] : ["Pick at least one."])
]

// Controlled — the parent owns the value; OnChange (auto-wrapped) re-renders it.
RadioGroup(plans, Value: _plan, OnChange: v => _plan = v)
CheckboxGroup<string>(interests, Value: _interests, OnChange: next => _interests = next)
```

- Bound mode takes the `Bind` expression first; `Validate` fans into none/sync/async overloads like
  `Input` (§9). `RadioGroup` renders the option equal to the current value `checked` and sets the bound
  property on select; `CheckboxGroup` mutates the bound collection (membership by
  `EqualityComparer<TItem>.Default`) — you usually need the explicit `CheckboxGroup<string>` when the
  collection is a concrete `List<T>`. Each change calls `NotifyFieldChanged` + `NotifyFieldTouched` +
  `ValidateFieldAsync`, so DataAnnotations / FluentValidation rules apply.
- Each item renders Bootstrap 5.3
  [check markup](https://getbootstrap.com/docs/5.3/forms/checks-radios/) — a
  `<div class="form-check">` wrapping a `.form-check-input` and a `.form-check-label` tied together by
  `id`/`for`. `ItemClass` adds extra classes (e.g. `"form-check-inline"`); `OptionLabel` customizes the label.
- They are **Components** (their own re-render boundary), so a toggle re-renders the control itself; for
  host-side derived UI (a live summary) use **controlled** mode — the auto-wrapped `OnChange` re-renders
  the host. (In bound mode, feedback lives inside the control via the embedded `ValidationMessage`.)

See `samples/Rask.Example.Shared/Features/FormGroups/FormGroupsPage.cs`.

## 9. Building form components (public binding API)

Rask doesn't ship a large control library — instead it exposes a small public API in `Rask.Core.Forms`
so you can write exactly the controls you need. `RadioGroup`/`CheckboxGroup` (§8) and the showcase
`MultiSelect<TItem>` are built entirely on it. This section is the recipe.

### The binding API

- **`ExpressionAccessor.Parse(Expression)` → `Accessor`** — turns a `() => model.Prop` lambda into a
  runtime accessor: `Target` (the owner instance), `Getter()`/`Setter(value)`, `PropertyName`,
  `PropertyType`, and `Field` (the `FieldIdentifier`). Accepts simple properties, nested chains,
  foreach-captured locals, and indexer access (e.g. `() => model.Items[i].Name`).
- **`BindingHelpers.ResolveBindingContext(object model)` → `EditContext?`** — resolves the `EditContext`
  the surrounding `Form` will use (returns `null` outside a form/live render).
- **`BindingHelpers.FormatValue(object?)` → `string`** — the framework's value→string convention
  (invariant culture; the shapes `<input>` round-trips).
- **`EditContext.RegisterFieldValidator(field, validate, valueGetter)`** — register a per-field
  validator (a `Func<T, IEnumerable<string>>` or async
  `Func<T, CancellationToken, ValueTask<IEnumerable<string>>>`). Always call it each render — passing
  `null` clears a stale rule.
- **`BindingHelpers.SetCollectionMembership(collection, item, include, comparer?)`** — add (when absent)
  or remove (the matched instance) an item in a bound `ICollection<T>` by a comparer
  (default `EqualityComparer<T>.Default`); returns whether it changed. The membership edit a multi-select
  / checkbox-group performs per toggle.
- **`BindingHelpers.NotifyAndValidateFieldAsync(ctx, field)`** — commits a change: marks the field
  changed + touched and re-validates it (no-op when `ctx` is `null`). The one call a custom control's
  change handler needs.

A bound control reads the value during `Render`, registers any validator, and on each interaction
mutates the value then commits the field:

```csharp
var acc = ExpressionAccessor.Parse(Bind);                 // Bind: Expression<Func<ICollection<T>>>
var ctx = BindingHelpers.ResolveBindingContext(acc.Target);
ctx?.RegisterFieldValidator(acc.Field, Validate, () => acc.Getter());   // null clears a stale rule
var selected = acc.Getter() as ICollection<T>;
// …render options from `selected`…
// in a click/change handler, after toggling the bound collection:
BindingHelpers.SetCollectionMembership(selected!, item, include: nowChecked);
await BindingHelpers.NotifyAndValidateFieldAsync(ctx, acc.Field);
```

Surface field messages with `ValidationMessage(Bind, …)` (§2.Rendering messages) inside the control.

### The `IFormControl<T>` contract (generator-synthesized factories)

For a **generic** component, the cleanest path is to implement `IFormControl<T>` (in `Rask.Core.Forms`) and
let the generator synthesize **both** factories — you write no `Bound` method and no factory plumbing:

```csharp
public sealed class CheckboxGroup<TItem> : Component, IFormControl<ICollection<TItem>>
{
    public required IEnumerable<TItem> Options { get; set; }

    // controlled
    public ICollection<TItem>? Value { get; set; }
    public Callback<ICollection<TItem>>? OnChange { get; set; }
    public CallbackAsync<ICollection<TItem>>? OnChangeAsync { get; set; }

    // bound
    public Expression<Func<ICollection<TItem>>>? Bind { get; set; }
    public Validate<ICollection<TItem>>? Validate { get; set; }
    public ValidateAsync<ICollection<TItem>>? ValidateAsync { get; set; }
    public Action<ICollection<TItem>>? AfterBind { get; set; }
    public Func<ICollection<TItem>, Task>? AfterBindAsync { get; set; }

    public string? ItemClass { get; set; }   // shared/display — on both factories
    protected override RenderResult Render() { /* … */ }
}
```

The generator emits:
- a **controlled** factory `CheckboxGroup<TItem>(Options, Value: …, OnChange: …, …)` — the bound members are
  excluded automatically (no `[SkipFactory]`), and `OnChange`/`OnChangeAsync` are auto-wrapped so invoking
  them re-renders the host;
- a **bound** factory `CheckboxGroup<TItem>(() => model.Tags, Options, …)` — Bind-first, with the validator
  fanned into none/sync/async overloads (`Validate:` accepts a sync `Validate<T>` or async `ValidateAsync<T>`).

All members are keyed on one value type `T`. In `Render`, collapse the typed validators for the
`EditContext`:

```csharp
ctx?.RegisterFieldValidator(fid, (Delegate?)Validate ?? ValidateAsync, () => acc.Getter());
```

(The built-in `Input<T>`/`Select<T>`/`Textarea<T>` implement `IFormControl<T>` too: bound usage infers `T`
from the expression (`Input(() => model.Age)` → `Input<int>`, type derived from `T`), while plain usage takes
the explicit argument — `Input<string>("text", …)`. They resolve binding at render time via the same
interface helpers (`RegisterValidator`/`ControlledChangeHandler`).)

### Stateless (`Fragment`) vs stateful (`Component`) — and host re-render

This is the one subtlety worth understanding:

- A control with **no view state of its own** *can* be a static factory returning a `Fragment`. Its
  `<input>` handlers are owned by the **host** component that declared it (handler-owner resolution keeps
  the owner as the host when the handler isn't a `Component`-targeted delegate), so a change re-renders
  the host for free — exactly like a bound `Input`. Host-side derived UI (a live summary) just updates.
- A control written as a **`Component`** (so the generator can emit its factory, or because it needs view
  state like an open/closed dropdown) is its own re-render boundary: its handlers re-render *it*, not the
  host, so host-side derived UI would go stale. Two clean options:
  - keep the live feedback **inside the control** (chips, an embedded `ValidationMessage`) — it refreshes
    because the control re-renders itself; or
  - expose an `OnChange` **callback** the consumer passes. Callback props on a component are auto-wrapped
    (`AutoCallback`) so invoking them re-renders the component that owns the handler — i.e. the host — so a
    host-side summary updates with no `StateHasChanged`. (The sample `CheckboxGroup`/`RadioGroup`/`MultiSelect`
    are all Components and use exactly this for their controlled mode.)

### Bound vs controlled

Mirror `Input` and offer both shapes where it makes sense:

- **Bound** — take a `Bind` expression and drive the `EditContext` (validation, touched-tracking). Add
  `AfterBind`/`AfterBindAsync(value)` post-mutation hooks for consumer logic.
- **Controlled** — take a `Value` + `OnChange`/`OnChangeAsync(newValue)` pair and let the parent own the
  state (no `EditContext`, so no validation). Build a *new* collection rather than mutating `Value`.

### Worked examples

- `samples/Rask.Example.Shared/Shared/CheckboxGroup.cs` / `RadioGroup.cs` — Components with bound +
  controlled modes and a per-field `Validate` rule, rendering Bootstrap `form-check` markup.
- `samples/Rask.Example.Shared/Shared/MultiSelect.cs` — a stateful `Component`: a Bootstrap dropdown
  with removable chips, Esc / click-outside close (pure live-diff, no client JS), both bound (with a
  `Validate` rule) and controlled (`Value` + auto-wrapped `OnChange`) modes. See the `/multiselect` page.
