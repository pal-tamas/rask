# Forms — nested models & control groups

Nested/complex models, radio & checkbox groups, and building your own form controls.

‹ Back to [Forms & validation](forms.md)

## Nested / complex models

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
Input(() => _model.Address.Street),
ValidationMessage(For: () => _model.Address.Street, Template: errs => Div(Class: "err")[errs[0]]),
```

**Collection binding — `foreach` + per-item capture** (the canonical pattern). Each iteration closes
over a distinct `item`, so each row's lambda targets its own instance:

```csharp
foreach (var item in _model.Items)
{
    rows.Add(Tr()[
        Td()[Input(() => item.Description)],
        Td()[Input(() => item.Quantity)],
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
        Td()[Input(() => _model.Items[i].Description)],
        Td()[Input(() => _model.Items[i].Quantity)]
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

The four patterns, live — sub-object binding, `foreach` and indexer collection binding, and
FluentValidation nesting:

<!-- demo:nested-subobject -->

<!-- demo:nested-list-foreach -->

<!-- demo:nested-list-indexer -->

<!-- demo:nested-fluent -->

A nested graph with **async** validators and live totals rolling up from the rows:

<!-- demo:validation-nested-async -->

---

## Radio & checkbox groups (example components)

`RadioGroup<TValue>` binds one value from a set of options; `CheckboxGroup<TItem>` binds an
`ICollection<TItem>`. Typed, production-ready versions ship in the optional **`Rask.Bootstrap`** package as
`BsRadioGroup` / `BsCheckboxGroup` / `BsMultiSelect` (see [bootstrap.md](bootstrap.md)). The versions below are
a **copyable worked example** of the binding API of §9 (`samples/Rask.Example.Shared/Shared/`) — `IFormControl<T>`
is the framework primitive; the control is yours to build or take from the package. They're structured exactly like
`MultiSelect<TItem>`, with **bound** and **controlled** modes (so the generator emits their factories):

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
- On the `Rask.Bootstrap` `BsRadioGroup`/`BsCheckboxGroup`, pass `Label:` to give the group an accessible
  name: the options are then wrapped in a `<fieldset>` titled by a `<legend>`, which is the correct
  grouping semantics for a set of related radios/checkboxes. Without a `Label` you get the bare per-item
  fragment (so you can supply your own `<fieldset>`/heading). An unnamed control derives a page-unique
  fallback `name`, so two on one page are never merged into a single browser radio group.
- They are **Components** (their own re-render boundary), so a toggle re-renders the control itself; for
  host-side derived UI (a live summary) use **controlled** mode — the auto-wrapped `OnChange` re-renders
  the host. (In bound mode, feedback lives inside the control via the embedded `ValidationMessage`.)
- **Reading validation state in a custom control just works.** If you bake feedback straight into your
  own `Render()` — reading `EditContext.GetValidationMessages(field)` / `GetValidationEntries()` /
  `ShouldShowValidatingIndicator(field)` — the framework detects the read and opts that control out of
  its render cache automatically, so a message produced later in the submit pipeline always repaints. No
  `StateHasChanged()`, no `BypassRenderCache` override (the same auto-opt-out `Context.Get` consumers get).

`RadioGroup` (single value) and `CheckboxGroup` (a collection), live:

<!-- demo:form-groups -->

**Multi-select.** `MultiSelect<TItem>` (and the `Rask.Bootstrap` `BsMultiSelect<T>`) binds an
`ICollection<TItem>` through a dropdown of chips — bound and controlled, with checkbox and radio
option renderings. The single-value twin is `BsSelect<T>`: same data-driven API (`Options` +
`OptionLabel`) and custom `.dropdown-menu` listbox (zero-JS, keyboard + ARIA `combobox`/`listbox`),
binding one `TItem`; `Native: true` falls back to the plain OS `<select>`.

**A plain `<select multiple>` bound to a collection.** `Select(() => …).Multiple(true)` binds the
whole selection when `T` is a string collection — `string[]`, `List<string>`, `HashSet<string>`, or the
`IReadOnlyList<string>` / `IList<string>` / `ICollection<string>` / `IEnumerable<string>` interfaces:

```csharp
Select(() => model.Tags).Multiple(true)[
    Option("news"), Option("sport"), Option("weather")
]
```

Every picked option is marked on render, and each change replaces the collection rather than editing its
membership — the browser reports the *absolute* selection every time, so a replace re-syncs the model
even if an intermediate render was coalesced.

Two limits worth knowing:

- **The element type is `string`.** The reflective version that would accept any parsable element needs
  `MakeGenericType` and `Array.CreateInstance`, both of which are AOT-hostile — and
  `samples/Rask.Example.Wasm` has to publish with zero trim warnings. Bind `string[]` and convert.
- **`Multiple: true` over a scalar property keeps the single-value binding.** That is a model which can
  only hold one answer; widening it silently would be the more surprising behaviour.

<!-- demo:multi-select-native -->

<!-- demo:multi-select -->

<!-- demo:multi-select-controlled -->

<!-- demo:multi-select-checkbox -->

<!-- demo:multi-select-radio -->

## Surviving a redeploy

If the server is replaced while someone is filling a form in, the page may have to reload — and the
fields they had edited are put back. It is a three-way merge, so a field is only re-applied when the
replacement server rendered the same value the old one had: if its state changed in the meantime, the
server wins and the stale edit is dropped. Whatever *is* restored is pushed back over the socket, so the
model matches what the page shows. [Shutdown and redeploy](configuration.md#shutdown-and-redeploy) has the
full rules.

Two things to know when building a form:

- **A field needs an `id` or a `name`.** A bound `Input` gets a `name` from the bound property for free,
  so this is usually nothing to think about — but a key that matches more than one control on the page is
  skipped rather than guessed at, and a control with neither is never restored.
- **`data-rask-no-restore` opts out** a field, or every field under it:

```csharp
Div(Data: new Dictionary<string, string?> { ["rask-no-restore"] = "" })[
    Input(() => _model.CouponCode).Class("form-control")   // never carried across a reload
]
```

Passwords, file, hidden and one-time-code inputs, and anything with a `cc-*` / `current-password` /
`new-password` `autocomplete`, are excluded unconditionally — they never reach `sessionStorage` at all.
`<select>` isn't restored yet.

## Building your own form controls

The binding system is public: a custom control implementing `IFormControl<T>` gets generator-synthesized
bound + controlled factories, per-field validation, and the same ergonomics as the built-ins — see the
dedicated guide **[building-form-controls.md](building-form-controls.md)** (with a complete worked example
and the `IFormControl<T>` helper reference). `RadioGroup`/`CheckboxGroup` (§8) and the showcase
`MultiSelect<TItem>` are built entirely on it.
