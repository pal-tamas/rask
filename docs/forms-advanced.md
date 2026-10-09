# Forms — nested models & control groups

Nested/complex models, radio & checkbox groups, and building your own form controls.

‹ Back to [Forms & validation](forms.md)

## Nested / complex models

`Bind` and validation extend transparently through sub-objects and collections: a nested field takes
its `.Validate(…)` rule exactly as a top-level one does. `FieldIdentifier` is **reference-based** (keyed off the
owner sub-instance, not a dotted path from the root), so removing or replacing a row drops its error
state with it.

```csharp
public sealed class CheckoutModel
{
    public string Name { get; set; } = "";
    public AddressModel Address { get; set; } = new();
    public List<LineItem> Items { get; set; } = new();
}
public sealed class AddressModel
{
    public string Street { get; set; } = "";
    public string Country { get; set; } = "";
}
```

**Sub-object binding** uses the same `.Bind(() => …)` shape:

```csharp
Input.Bind(() => _model.Address.Street)
    .Validate(v => v.Length > 0 ? [] : ["Street is required."]),
Validation.Message.Template(errs => Div.Class("err")[errs[0]]).For(() => _model.Address.Street),
```

**Attributes on a nested model are also supported.** Where the models carry DataAnnotations — as the
demos on this page do — the form's built-in pass covers the whole reachable graph, with no per-level
opt-in and nothing declared.

**Collection binding — `foreach` + per-item capture** (the canonical pattern). Each iteration closes
over a distinct `item`, so each row's lambda targets its own instance:

```csharp
foreach (var item in _model.Items)
{
    rows.Add(Tr[
        Td[Input.Bind(() => item.Description)],
        Td[Input.Bind(() => item.Quantity)],
        Td[Button.Type(ButtonType.Button).OnClick(() => _model.Items.Remove(item))["×"]]
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
    rows.Add(Tr[
        Td[$"#{i + 1}"],
        Td[Input.Bind(() => _model.Items[i].Description)],
        Td[Input.Bind(() => _model.Items[i].Quantity)]
    ]);
}
```

`foreach` has no closure trap. Records with init-only properties can't be auto-bound through the
setter — declare them `{ get; set; }`, or use the indexer pattern with a manual handler that replaces
the slot (`_model.Items[i] = _model.Items[i] with { Field = newValue }`).

**FluentValidation nesting** uses `SetValidator(...)` and `RuleForEach(...).SetValidator(...)`; Rask
routes the dotted `error.PropertyName` (`Address.Street`, `Lines[0].Quantity`) back to the runtime
sub-instance so `Validation.Message.Template(…).For(() => _model.Address.Street)` reads the right slot.

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

## Radio & checkbox groups

`Ui.RadioGroup` binds one value; `Ui.CheckboxGroup` binds an `ICollection<T>`. Both are
[Flux UI's](ui-kit.md#checkbox-radio-and-switch), and both are ordinary `IFormControl<T>` controls — the binding
API of §9 — so they work **bound** or **controlled**, exactly like `Input`, and you can build a group of
your own the same way ([building-form-controls.md](building-form-controls.md)). The choices are the group's
**children**: a `Ui.Radio` or a `Ui.Checkbox` whose `Value` is what the group binds.

```csharp
// Bound — two-way binds the model, with an optional per-field Validate rule.
Form.Model(_prefs)[
    Ui.RadioGroup.Bind(() => _prefs.Plan).Label("Plan")[                     // single value
        Ui.Radio.Value(Plan.Free).Label("Free"),
        Ui.Radio.Value(Plan.Pro).Label("Pro"),
        Ui.Radio.Value(Plan.Team).Label("Team")
    ],

    Ui.CheckboxGroup.Bind(() => _prefs.Interests).Label("Interests")         // a collection
        .Validate(tags => tags.Count >= 1 ? [] : ["Pick at least one."])[
        Ui.Checkbox.Value("web").Label("Web"),
        Ui.Checkbox.Value("mobile").Label("Mobile"),
        Ui.Checkbox.Value("ai").Label("AI")
    ]
]

// Controlled — the parent owns the value; OnChange (auto-wrapped) re-renders it.
Ui.RadioGroup.Value(_plan).Label("Plan").OnChange(v => _plan = v)[ … ]
Ui.CheckboxGroup.Values(_interests).Label("Interests").OnChange(next => _interests = next)[ … ]
```

- Bound mode opens with `Bind`; `Validate` takes a synchronous or an asynchronous rule, like `Input`
  (§9). `Ui.RadioGroup` renders the radio whose `Value` equals the current value `checked` and sets the
  bound property on select; `Ui.CheckboxGroup` writes the bound collection, in the order the checkboxes
  are written (membership by `EqualityComparer<T>.Default`). Each change calls `NotifyFieldChanged` +
  `NotifyFieldTouched` + `ValidateField`, so DataAnnotations / FluentValidation rules apply.
- A choice takes `Label`, `Description`, `Disabled` and (in a card, a segment or a button) `Icon`. The
  group's variant picks the look — `.Cards`, `.Pills`, `.Buttons`, and `.Segmented` on a radio group —
  while keeping a real `<input type="radio">`/`<input type="checkbox">` inside each label, so the arrow
  keys, the space bar and the form post are the browser's. `Ui.CheckboxAll` among the checkboxes ticks
  or clears them all, and shows a dash while only some are ticked.
- Give the group a `Label`: it names the group's container for a screen reader (`aria-labelledby`).
  Without a `Name`, the radios share the group's own id as their `name`, so two groups on one page are
  never merged into a single browser radio group.
- They are **Components** (their own re-render boundary), so a toggle re-renders the control itself; for
  host-side derived UI (a live summary) use **controlled** mode — the auto-wrapped `OnChange` re-renders
  the host. (In bound mode, feedback lives inside the control via the embedded `Validation.Message`.)
- **Reading validation state in a custom control just works.** If you bake feedback straight into your
  own `Render()` — reading `EditContext.GetValidationMessages(field)` / `GetValidationEntries()` /
  `ShouldShowValidatingIndicator(field)` — the framework detects the read and opts that control out of
  its render cache automatically, so a message produced later in the submit pipeline always repaints. No
  `StateHasChanged()`, no `BypassRenderCache` override (the same auto-opt-out `Context.Get` consumers get).

`Ui.RadioGroup` (single value) and `Ui.CheckboxGroup` (a collection), live:

**A drawn single-select.** [`UiSelect<T>`](ui-kit.md#select) binds one `T`, takes its options as children
(`Ui.SelectOption`) and renders the platform's `<select>` by default; `.Listbox` draws the list itself
instead — a native `popover` `role="listbox"` under a `role="combobox"` button, with the arrow keys, Enter,
type-ahead and an `aria-activedescendant` cursor that skips unavailable options — and `.Combobox` makes the
box a text input that filters it. Reach for one when an option has to carry more than the platform will
show (an icon, a description, an avatar) or the list has to be searched. The drawn list needs the runtime;
the native one does not.

**A plain `<select multiple>` bound to a collection.** `Select.Bind(() => …).Multiple()` binds the
whole selection when `T` is a string collection — `string[]`, `List<string>`, `HashSet<string>`, or the
`IReadOnlyList<string>` / `IList<string>` / `ICollection<string>` / `IEnumerable<string>` interfaces:

```csharp
Select.Bind(() => model.Tags).Multiple()[
    Option.Value("news")["News"], Option.Value("sport")["Sport"], Option.Value("weather")["Weather"]
]
```

Every picked option is marked on render, and each change replaces the collection rather than editing its
membership — the browser reports the *absolute* selection every time, so a replace re-syncs the model
even if an intermediate render was coalesced.

Two limits worth knowing:

- **The element type is `string`.** The reflective version that would accept any parsable element needs
  `MakeGenericType` and `Array.CreateInstance`, both of which are AOT-hostile — and
  `src/Rask.Site` has to publish with zero trim warnings. Bind `string[]` and convert.
- **`.Multiple()` over a scalar property keeps the single-value binding.** That is a model which can
  only hold one answer; widening it silently would be the more surprising behaviour.

**Taking the picked values yourself.** `OnSelect` (a synchronous or an asynchronous handler) hands over the
raw option values the user picked, as `IReadOnlyList<string>` — the whole selection every time, never a delta:

```csharp
Select.Of<string>().Multiple().OnSelect(picked => _chosen = Map(picked))[
    Option.Value("news")["News"], Option.Value("sport")["Sport"], Option.Value("weather")["Weather"]
]
```

It is the way past the string-element limit above: a control that rendered its own options already
knows how to turn those values back into its own type, so it needs none of the binding machinery that
limit belongs to. The kit's collection-bound select ([`UiSelectMultiple<T>`](ui-kit.md#select)) is generic over any `T` — an
int, an enum, a Guid — for the same reason: it draws its own options and maps a pick back itself, while
this control cannot. Controlled mode only, and it takes precedence over
`OnChange`: both write the one `data-rask-on-change` attribute, so a control cannot have two.

**The kit has a control for this.** Everything above is the raw `<select>`. For a field a person fills
in, [`Ui.Select`](ui-kit.md#select) opened on a collection with `.Listbox.Multiple()` is the one to reach
for — a search field (`Searchable()`), a keyboard, a button that names or counts what is picked, and a drawn
list that stays open while you pick.

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
Div.Data("rask-no-restore")[
    Input.Bind(() => _model.CouponCode).Class("w-full rounded-md border border-slate-300 bg-white px-3 py-1.5 text-sm text-slate-900 placeholder:text-slate-400 focus:border-violet-500 focus:outline-none dark:border-slate-600 dark:bg-slate-900 dark:text-slate-100")   // never carried across a reload
]
```

Passwords, file, hidden and one-time-code inputs, and anything with a `cc-*` / `current-password` /
`new-password` `autocomplete`, are excluded unconditionally — they never reach `sessionStorage` at all.
`<select>` isn't restored yet.

## Building your own form controls

The binding system is public: a custom control implementing `IFormControl<T>` gets generator-synthesized
bound + controlled chains, per-field validation, and the same ergonomics as the built-ins — see the
dedicated guide **[building-form-controls.md](building-form-controls.md)** (with a complete worked example
and the `IFormControl<T>` helper reference). `Ui.RadioGroup`/`Ui.CheckboxGroup` (§8) and
`Ui.Select` are built entirely on it.
