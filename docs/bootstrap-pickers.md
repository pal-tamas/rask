# Bootstrap — date & time pickers

The hand-editable date/time controls from [`Rask.Bootstrap`](bootstrap.md) — `BsDatePicker<T>`,
`BsTimePicker<T>`, and `BsDateTimePicker<T>`. All are `IFormControl<T>`-bound, bind two-way through
`Form<T>` with built-in validation, and run with **zero `bootstrap.js`**.

They are **hand-editable**: the box is a text `<input>` you can type into (parsed live per keystroke in
`CultureInfo.CurrentCulture`; a partial/invalid entry is kept, not reverted, and blur normalises it),
and focusing it opens a custom calendar/clock **popover** (a month grid + hour/minute lists) driven
entirely by Rask live-diff state — no `bootstrap.js`. They bind `DateOnly`/`TimeOnly`/`DateTime` (and
their nullable + `DateTimeOffset` forms), localize the weekday order/names and month label from
`CultureInfo.CurrentCulture`, and constrain selection with `Min`/`Max`/`Disable`. A nullable value
gets a clear (×) button; `Native: true` degrades to the native
`<input type=date|time|datetime-local>`.

```csharp
BsDatePicker(() => model.StartsOn, Min: DateOnly.FromDateTime(DateTime.Today))
BsTimePicker(() => model.At)
BsDateTimePicker(() => model.When, Native: true)   // native OS control
```

## Live example

The `BsDatePicker`/`BsTimePicker`/`BsDateTimePicker` calendar/clock controls:

<!-- demo:bootstrap-pickers -->

The pickers open the same fixed-position `.dropdown-menu` popover used by the dropdown and the
comboboxes — see [modals, offcanvas & dropdowns](bootstrap-overlays.md) for the details and the one
browser-rule caveat.
