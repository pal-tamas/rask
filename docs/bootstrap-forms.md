# Bootstrap — forms & inputs

The `IFormControl<T>`-bound form controls from [`Rask.Bootstrap`](bootstrap.md) — text inputs, the
searchable `BsSelect`/`BsMultiSelect` comboboxes, and the hand-editable date/time pickers. All bind
two-way through `Form<T>` with built-in validation (`.is-invalid` + `.invalid-feedback`), are
keyboard-navigable, and run with **zero `bootstrap.js`**.

See also: [Bootstrap components](bootstrap.md) (setup, layout, utilities) ·
[Navigation & overlays](bootstrap-navigation.md) (navbar, tabs, modal, toast, dropdown).

```csharp
BsInput(() => model.Email, Label: "Email", Type: InputType.Email)   // .is-invalid + .invalid-feedback built in
BsSelect(() => model.Plan, plans, OptionLabel: p => Text(p), Filter: (p, t) => p.Contains(t, StringComparison.OrdinalIgnoreCase))
```

## Live examples

Every control below is driven entirely by Rask's live runtime — **no `bootstrap.js`** is loaded.

**Forms** — `IFormControl<T>`-bound controls with built-in validation. `BsSelect<T>` is a custom combobox —
a `.form-select` display box (showing the option's rich `OptionLabel`) that opens a `.dropdown-menu` listbox
(data-driven `Options` + `OptionLabel`). Pass a **`Filter` predicate** (`(item, text) => bool`) to add a
**search field in the dropdown** that narrows the options as you type; a nullable binding gets an `×` clear.
`BsMultiSelect<T>` is the same but multi-value, with the chosen items shown as chips (and the same opt-in
`Filter`). Both are zero-JS live-diff, keyboard-navigable, ARIA `combobox`/`listbox`: opening a searchable
select focuses the filter so you can type at once, and the navigation/commit keys stay inside the open
dropdown — **Enter picks the highlighted option without submitting the surrounding form**, Escape closes.
`Native: true` drops
`BsSelect` back to the plain OS `<select>` (handy on mobile). To bind a **projected field** while the options
are objects, add an `OptionValue` selector — `BsSelect(() => model.PersonId, people, OptionValue: p => p.Id,
OptionLabel: p => Text(p.Name))` binds the id but renders/searches the whole `Person`:

<!-- demo:bootstrap-forms -->

**`BsSelect<T>` variants** — the same control across every option: basic (binds the option), `Floating`
label, searchable (`Filter`), nullable + `×` clear, `OptionValue` projected-id binding, `Native` OS
`<select>`, native + nullable, and `Disabled`. Each is bound and echoed by a live readout:

<!-- demo:bootstrap-select -->

**`BsMultiSelect<T>` variants** — chips + a checkable dropdown bound to a collection: basic, searchable
(`Filter`), `Floating`, and `Disabled`:

<!-- demo:bootstrap-multiselect -->

**Date & time pickers** — `BsDatePicker<T>`/`BsTimePicker<T>`/`BsDateTimePicker<T>` are **hand-editable**:
the box is a text `<input>` you can type into (parsed live per keystroke in `CultureInfo.CurrentCulture`;
a partial/invalid entry is kept, not reverted, and blur normalises it), and focusing it opens a custom
calendar/clock **popover** (a month grid + hour/minute lists) driven entirely by Rask live-diff state — no
`bootstrap.js`. They bind `DateOnly`/`TimeOnly`/`DateTime` (and their nullable + `DateTimeOffset` forms),
localize the weekday order/names and month label from `CultureInfo.CurrentCulture`, and constrain selection
with `Min`/`Max`/`Disable`. A nullable value gets a clear (×) button; `Native: true` degrades to the native
`<input type=date|time|datetime-local>`:

<!-- demo:bootstrap-pickers -->

The selects, multiselect and pickers all open the same fixed-position `.dropdown-menu` popover — a tiny
runtime helper re-anchors it with `position: fixed` while open (opt-in via `data-rask-popover`) so it
escapes any `overflow: hidden/auto` ancestor and tracks the trigger on scroll/resize. See
[navigation & overlays](bootstrap-navigation.md) for the details and the one browser-rule caveat.
