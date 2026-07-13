# Bootstrap — selects & multiselect

The data-driven comboboxes from [`Rask.Bootstrap`](bootstrap.md) — `BsSelect<T>` and
`BsMultiSelect<T>`. Both are `IFormControl<T>`-bound, bind two-way through `Form<T>` with built-in
validation, and run with **zero `bootstrap.js`**. For the plain inputs see
[form controls](bootstrap-forms.md).

```csharp
BsSelect(() => model.Plan, plans, OptionLabel: p => Text(p), Filter: (p, t) => p.Contains(t, StringComparison.OrdinalIgnoreCase))
```

`BsSelect<T>` is a custom combobox — a `.form-select` display box (showing the option's rich
`OptionLabel`) that opens a `.dropdown-menu` listbox (data-driven `Options` + `OptionLabel`). Pass a
**`Filter` predicate** (`(item, text) => bool`) to add a **search field in the dropdown** that narrows
the options as you type; a nullable binding gets an `×` clear. `BsMultiSelect<T>` is the same but
multi-value, with the chosen items shown as chips (and the same opt-in `Filter`). Both are zero-JS
live-diff, keyboard-navigable, ARIA `combobox`/`listbox`: opening a searchable select focuses the
filter so you can type at once, and the navigation/commit keys stay inside the open dropdown —
**Enter picks the highlighted option without submitting the surrounding form**, Escape closes.
`Native: true` drops `BsSelect` back to the plain OS `<select>` (handy on mobile). To bind a
**projected field** while the options are objects, add an `OptionValue` selector —
`BsSelect(() => model.PersonId, people, OptionValue: p => p.Id, OptionLabel: p => Text(p.Name))` binds
the id but renders/searches the whole `Person`.

## `BsSelect<T>` variants

The same control across every option: basic (binds the option), `Floating` label, searchable
(`Filter`), nullable + `×` clear, `OptionValue` projected-id binding, `Native` OS `<select>`, native +
nullable, and `Disabled`. Each is bound and echoed by a live readout:

<!-- demo:bootstrap-select -->

## `BsMultiSelect<T>` variants

Chips + a checkable dropdown bound to a collection: basic, searchable (`Filter`), `Floating`, and
`Disabled`:

<!-- demo:bootstrap-multiselect -->

The selects and multiselect open the same fixed-position `.dropdown-menu` popover used by the
dropdown and the pickers — see [modals, offcanvas & dropdowns](bootstrap-overlays.md) for the details
and the one browser-rule caveat.
