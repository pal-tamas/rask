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
BsDatePicker(() => model.StartsOn).Min(DateOnly.FromDateTime(DateTime.Today))
BsTimePicker(() => model.At)
BsDateTimePicker(() => model.When).Native(true)   // native OS control
```

## Time range, seconds & steps

`BsTimePicker` takes `Min`/`Max` (`TimeOnly`) to bound the clock, and `MinuteStep` (default 5) to
control the minute list. Set `Seconds: true` to add a seconds column stepped by `SecondStep` (default
5). `BsDateTimePicker` also takes `Seconds`/`SecondStep`; its calendar honours the `DateTime` `Min`/`Max`
day-by-day, and on a boundary day the time columns grey out the out-of-range hours/minutes. Out-of-range
values are always clamped back into `[Min, Max]` on write, whatever path produced them.

```csharp
BsTimePicker(() => model.At).Min(new TimeOnly(9, 0)).Max(new TimeOnly(17, 0)).Seconds(true).SecondStep(15)
BsDateTimePicker(() => model.When).Seconds(true)
```

## Keyboard

The calendar and clock are fully keyboard-operable (WAI-ARIA combobox + grid pattern — the box keeps
focus and `aria-activedescendant` tracks a virtual cursor). A first navigation key opens the popover:

| Key | Date / date-time grid | Time columns |
| --- | --- | --- |
| `←` / `→` | previous / next day | — |
| `↑` / `↓` | previous / next week | nudge the minute by `MinuteStep` (`Shift`: the second) |
| `PageUp` / `PageDown` | previous / next month (`Shift` → year) | nudge the hour |
| `Home` / `End` | start / end of the week | earliest / latest time (`Min`/`Max` or day edge) |
| `Enter` | select the navigated day | commit / close |
| `Escape` | close | close |

Typing into the box still commits live in parallel, so both entry styles work. `Enter` selects only a day
you actually arrow-navigated to, so pressing it after clearing a nullable field leaves the field blank; and
`←`/`→` still move the text caret (the day cursor moves alongside).

## Localizing the chrome

The weekday/month names and the per-date accessible labels come from `CultureInfo.CurrentCulture`
automatically. The remaining chrome that has no culture source — the month-nav buttons, the time-column
headings and the clear button — is translated with `Labels`:

```csharp
BsDatePicker(() => model.StartsOn).Labels(new BsPickerLabels
{
    PreviousMonth = "Vorheriger Monat",
    NextMonth = "Nächster Monat",
    Clear = "Löschen",
})
```

## Live example

The `BsDatePicker`/`BsTimePicker`/`BsDateTimePicker` calendar/clock controls:

<!-- demo:bootstrap-pickers -->

The pickers open the same fixed-position `.dropdown-menu` popover used by the dropdown and the
comboboxes — see [modals, offcanvas & dropdowns](bootstrap-overlays.md) for the details and the one
browser-rule caveat.
