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
BsRadioGroup(() => model.Plan, options)
```

`BsFormGroup`, `BsFormLabel` and `BsInputGroup`(+`BsInputGroupText`) compose labels, help text and
input add-ons around any control.

## Live example

`IFormControl<T>`-bound controls with built-in validation — driven entirely by Rask's live runtime,
**no `bootstrap.js`**:

<!-- demo:bootstrap-forms -->
