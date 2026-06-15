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

## 8. Radio & checkbox groups

`RadioGroup<TValue>` binds one value from a set of options; `CheckboxGroup<TItem>` binds an
`ICollection<TItem>`, toggling each item in place. Both are transparent `Fragment`s built on the same
binding machinery, so changes flow through the `EditContext` (validation, touched-tracking) like any
bound field.

```csharp
public static Component RadioGroup<TValue>(
    Expression<Func<TValue>> Bind,
    IEnumerable<TValue> Options,
    Func<TValue, Child>? OptionLabel = null,
    string? Name = null,
    string? ItemClass = null,
    bool Disabled = false)

public static Component CheckboxGroup<TItem>(
    Expression<Func<ICollection<TItem>>> Bind,
    IEnumerable<TItem> Options,
    Func<TItem, Child>? OptionLabel = null,
    string? Name = null,
    string? ItemClass = null,
    bool Disabled = false)
```

```csharp
Form(_prefs)[
    RadioGroup(() => _prefs.Plan,                       // single value
        Options: new[] { Plan.Free, Plan.Pro, Plan.Team },
        OptionLabel: p => Span()[p.ToString()]),

    CheckboxGroup<string>(() => _prefs.Interests,       // a collection — toggles in place
        Options: new[] { "Web", "Mobile", "AI", "Games" },
        OptionLabel: t => Span()[t])
]
```

- The first positional argument is the `Bind` expression.
- `RadioGroup` renders the option equal to the current value `checked`; the change handler sets the
  bound property.
- `CheckboxGroup` mutates the bound collection in place; membership is compared with
  `EqualityComparer<TItem>.Default`. You usually need the explicit type argument
  (`CheckboxGroup<string>`) when the bound collection is a concrete `List<T>`.
- Each renders a transparent `Fragment` of `<label><input>…</label>` — control layout with
  `OptionLabel` and `ItemClass`.
- Changing an option re-renders the component that declared the group, so a live summary updates
  immediately. Each change calls `NotifyFieldChanged` + `NotifyFieldTouched` + `ValidateFieldAsync`,
  so DataAnnotations / FluentValidation rules on the bound property apply.

See `samples/Rask.Example.Shared/Features/FormGroups/FormGroupsPage.cs`.
