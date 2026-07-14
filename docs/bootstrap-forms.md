# Bootstrap — form controls

The `IFormControl<T>`-bound form controls from [`Rask.Bootstrap`](bootstrap.md) — text inputs,
checkboxes and radios, and the layout helpers. All bind two-way through `Form<T>` with built-in
validation (`.is-invalid` + `.invalid-feedback`), are keyboard-navigable, and run with **zero
`bootstrap.js`**. The Bootstrap form controls auto-wire `aria-invalid` + `aria-describedby` + a
`role="alert"` error region when a bound field is invalid.

The data-driven comboboxes and the date/time controls have their own pages:
[selects & multiselect](bootstrap-select.md) and [date & time pickers](bootstrap-pickers.md).

```csharp
BsInput(() => model.Email, Label: "Email", Type: InputType.Email)   // .is-invalid + .invalid-feedback built in
BsCheck(() => model.AcceptTerms, Label: "I accept the terms")
BsRadioGroup(() => model.Plan, options, Label: "Plan")              // <fieldset>/<legend> group
```

`BsFormGroup`, `BsFormLabel` and `BsInputGroup`(+`BsInputGroupText`) compose labels, help text and
input add-ons around any control.

## Attribute passthrough

`BsInput` forwards the full constraint / affordance surface of the core `Input` to the underlying
`<input>`, so a Bootstrap number, date, range, or file field is fully configurable:

```csharp
BsInput(() => model.Age, Label: "Age", Min: "0", Max: "120", Step: "1")
BsInput(() => model.Code, Label: "Code", Pattern: "[A-Z]{3}", MaxLength: 3, InputMode: "text")
BsInput(Type: InputType.File, Label: "Avatar", Accept: "image/*", Capture: "user", Multiple: true,
        OnFilesAsync: SaveAsync)
```

- **Constraints & affordances**: `Min`, `Max`, `Step`, `Pattern`, `MaxLength`, `MinLength`, `List`
  (datalist), `Autofocus`, `Autocomplete`.
- **File inputs**: `Accept`, `Capture`, `Multiple`.
- **Mobile / a11y hints**: `InputMode` (on-screen keyboard), `EnterKeyHint` (action-key label),
  `Spellcheck`.
- The HTML `size` attribute is intentionally *not* surfaced — `Size` on the Bootstrap controls is
  Bootstrap's control sizing (`form-control-sm` / `-lg`).

`BsTextarea` likewise forwards `Cols`, `MaxLength`, `MinLength`, `Autocomplete`, and `Autofocus`.

## Accessible groups

`BsRadioGroup`/`BsCheckboxGroup` take an optional `Label:` that names the group: the options are
wrapped in a `<fieldset>` titled by a `<legend>` (the correct grouping semantics + accessible name),
their `.invalid-feedback` is a `role="alert"` region, and each option carries `aria-invalid` +
`aria-describedby` when the field is invalid. Omit `Label` to keep the bare per-item fragment.

## Live example

`IFormControl<T>`-bound controls with built-in validation — driven entirely by Rask's live runtime,
**no `bootstrap.js`**:

<!-- demo:bootstrap-forms -->
