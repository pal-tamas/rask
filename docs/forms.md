# Forms & validation

Rask binds inputs two-way with a strongly-typed `Bind` expression and tracks per-field state (touched / modified /
messages / in-flight validation) on an `EditContext`. The same component code runs server-rendered or on WASM.

**A form that writes is the operation's.** `Form.For(Supply.Change, id)` is a form over an aggregate's own method:
its fields are the method's parameters, each value object's rule and the method's `Refuse` are shown under their
field as it is typed, and the submit runs the operation's entry — one aggregate, one transaction
([a form over an operation](#a-form-over-an-operation), [domain modelling](domain.md)). The rules live in the domain,
so the form repeats none of them.

This guide builds up in layers: binding → forms → the form over an operation → inline validation for a form that
writes nothing (a search box, a filter) → nested models → radio/checkbox groups.

For the analyzer IDs referenced here (`RASK001`, `RASK022`, …) see [diagnostics.md](diagnostics.md).

## On this page

- [Validation](forms-validation.md) — inline `.Validate(…)` on a field or the form, async rules, rules in a value object; then DataAnnotations (async attributes and messages from your own resources included) and FluentValidation.
- [Nested models & control groups](forms-advanced.md) — complex models, radio/checkbox groups, custom controls.

---

## 1. Two-way binding

The low-level path wires `Value` and an event handler yourself:

```csharp
Input.Value(_typed).Type(InputType.Text).OnInput(v => _typed = v)
P[$"Echo: {_typed}"]
```

<!-- demo:binding-manual -->

The ergonomic path is a `Bind` expression — one call replaces `Value` + `OnInput` + parsing:

```csharp
Input.Bind(() => _model.Name).Live().Placeholder("Your name")
P[$"Hello, {_model.Name}!"]
```

`.Live()` is there because the page reads the name back while it is typed. A bound field that is only read
when the form is saved does not need it — see [bind timing](#bind-timing).

<!-- demo:binding-typed -->

`Bind` is an `Expression<Func<TProp>>`. The chain step reads the expression and
derives everything from the bound property:

- **Input name** ← the property name (`name="Name"`). Override with `.Name(…)`.
- **Input type** ← the property's CLR type (`BindingHelpers.DefaultInputType`):
  `bool → checkbox`, every numeric primitive `→ number`, `DateOnly → date`,
  `DateTime`/`DateTimeOffset` `→ datetime-local`, `TimeOnly`/`TimeSpan` `→ time`, everything else
  `→ text`. Override with `.Type(…)` — an `InputType` enum value. The full set is
  `Text`, `Search`, `Tel`, `Url`, `Email`, `Password`, `Number`, `Checkbox`, `Radio`, `File`, `Range`,
  `Color`, `Date`, `DatetimeLocal` (renders `datetime-local`), `Time`, `Week`, `Month`, `Hidden`,
  `Button`, `Submit`, `Reset`, `Image`. A text-family type (`Text`/`Search`/`Tel`/`Url`/`Email`/
  `Password`) on a non-string bound input is a plain text field over that value: a number is shown and read in the
  reader's culture, anything else in the spelling its parser reads (`2026-11-19`, a `Guid`, an enum's name), so what
  the field shows reads back as the same value.
- **Passwords are the browser's.** An input of `type=password` (or `autocomplete` `current-password`,
  `new-password` or `one-time-code`) sends what was typed to the model and never gets it back: the server writes
  `""` to clear it and nothing else, so a model value set from the server does not reach the field, and a page
  that prefills a password is a bug in the page.
- **Update timing** — a bound control says nothing until the next action, and its value travels with
  that: see [bind timing](#bind-timing).

### Bind timing

**A bound control waits for the next action.** Typing into it, choosing in it and leaving it send nothing,
run no rule and render nothing. When the reader presses a button, submits, or does anything else the page
hears, everything they entered is sent first, in order, and then the action. This is how Livewire's
`wire:model` works, and Flux's forms with it.

Three steps after `Bind` say otherwise:

```csharp
Input.Bind(() => _m.Name)                                    // waits: the value travels with the next action
Input.Bind(() => _m.Title).Live()                            // as it is typed, after a 150 ms pause
Input.Bind(() => _m.Code).Live().Debounce(300.Milliseconds)  // a pause of your own
Input.Bind(() => _m.Notes).Blur()                            // on leaving the field
Select.Bind(() => _m.Country).Live()                         // as soon as it is picked
```

| Step | Livewire | Writes the model and runs the rules |
| --- | --- | --- |
| *(none)* | `wire:model` | with the next action |
| `.Live()` | `wire:model.live` | as the reader types, 150 ms after the last key; at once for a checkbox, a radio, a select, a range |
| `.Debounce(300.Milliseconds)` | `wire:model.live.debounce.300ms` | as the reader types, after that pause. Implies `.Live()`; `TimeSpan.Zero` sends every keystroke |
| `.Blur()` | `wire:model.live.blur` (`.blur` before v4) | on leaving the field — the default in a `Form.For` |
| `.Defer()` | `wire:model.defer` (v2) | with the next action, in a `Form.For` too |

**Say `.Live()` on a control you read while the reader is still in the form**: a greeting that echoes a
name, a character count, a total, a select that fills a second select, a checkbox that shows a section, a
filter. A control you only read in the submit handler needs nothing.

- **An action** is anything that reaches your code: a press on a button or a link with a handler, a
  submit, Enter in a field, a key an `OnKeyDown` hears, another control's `.Live()` or `.Blur()` change, a
  navigation, an event a kit component raises into your code (a tab with `OnChange`, a menu item, a
  dragged row). Each is preceded by the
  values that waited, so a handler never reads a model that has not heard what was typed.
- **A kit control drawing itself is not an action.** Opening a `Ui.Select`, a `Ui.Dropdown`, a date or a
  time picker, moving its cursor with the arrows or the pointer, typing in its search field, paging a
  calendar: the page hears those only because it is what draws the
  control, and in Flux they are no request at all. Nothing that waits is written for them, so a rule of a
  field above the list does not speak when the list is opened. A **pick** in a bound control that waits is
  the same — the pick itself waits. What does count: a pick in a control that says `.Live()` or is held
  with `Value`/`OnChange`, a search field with an `OnInput` of yours, a `Ui.Dropdown.OnToggle`, a
  `Ui.Tabs.OnChange`, a "create" row — anything that reaches your code.
- **What the page does by itself is not an action.** A toast whose time is up closing itself, a chart
  reporting the size it was measured at, a list scrolling, a video playing on, an animation ending, an
  image arriving, the reply to a JavaScript call, a call your script makes into .NET: none of them writes
  a field the reader is still typing in, and none runs its rules. Their handlers run; what waits goes on
  waiting for the reader.
- **A render the server starts** — a timer, a push, a handler that finishes later — cannot ask the browser
  first. It sees the model as of the last action, and it does not take back what the reader has entered
  since: the text stays in the field, the box stays ticked, and both still travel with the next action.
- **Validation follows the value.** A field that waits shows no message while it is typed into or left;
  its rules run when its value arrives, and all of them on submit. `.Live()`, `.Debounce(…)` and `.Blur()`
  fields validate when they write.
- **A message goes as soon as the reader starts correcting it** — at the first key, and no rule runs for
  that. This is the one thing a waiting field sends while it is typed into: a message with no value in
  it, once, and only while a message shows.
- **The unsaved-changes guard** ([`ConfirmLeave`](#ask-before-leaving-unsaved-changes)) counts the first
  keystroke, sent or not.
- **A rule still running when Save is pressed is not saved past**: the submit runs every rule again and
  waits for the answers.
- **`AfterBind`** runs when the value is written — with the action for a control that waits.

**In a form over an operation the default is `.Blur()`.** A field typed into under
[`Form.For(Note.Rename, id)`](#a-form-over-an-operation) — `Ui.Field`, `Ui.Input`, `Ui.Textarea`, `Ui.Phone`,
`Ui.Composer`, Core's `Input` and `Textarea`, in a `Ui.EditorDialog` or not — that names no step commits as the
reader leaves it, so the operation's dry run tells what the save would refuse right then, not with the next click.
A step it does name wins: `.Live()` and `.Debounce(…)` as above, and `.Defer()` — Livewire's old
`wire:model.defer` — to wait for the next action all the same (a field asked of the database, say). A hand form,
`Form.Model(m)`, keeps waiting; a control that is chosen rather than typed into waits in both.

```csharp
Form.For(Note.Rename, id)[f => [
    Ui.Field.Bind(() => f.Title),               // commits as it is left: Rename's Refuse shows then
    Ui.Field.Bind(() => f.Slug).Defer(),        // waits for the next action, as in a hand form
]]
```

`.Live()` is on every bound control of Core: `Input`, `Textarea`, `Select`. `.Debounce(…)`, `.Blur()` and `.Defer()`
are on `Input`, `Textarea`, `Ui.Input`, `Ui.Textarea` and `Ui.Field` (`.Defer()` on `Ui.Phone` and `Ui.Composer`
too), for a field that is typed into — text, number,
date. A checkbox, a radio, a range and a colour are chosen rather than typed: with either step they
simply write as they are chosen. A file input's files travel with the submit, as in any form; `OnFiles`
is an event of its own and is not bound. One thing defeats a pause, by sending the value early: an
`OnKeyDown` or `OnKeyUp` on the field that hears every key. Give it
[`data-rask-keys`](js-interop-runtime.md#keys-and-focus) and it hears only the keys it acts on.

The kit's `Ui.Input` and `Ui.Textarea` forward all three steps to Core's.

**Every chosen control of the kit follows the same rule**, and `.Live()` is on each of them: `Ui.Checkbox`,
`Ui.Switch`, `Ui.Select` (native, listbox, combobox, multiple), `Ui.Pillbox`, `Ui.Autocomplete`,
`Ui.CheckboxGroup`, `Ui.RadioGroup`, `Ui.Slider`, `Ui.Otp`, `Ui.Calendar`,
`Ui.DatePicker`, `Ui.TimePicker`, `Ui.Editor`, `Ui.Tabs`, `Ui.MenuCheckbox` and `Ui.MenuRadioGroup`.

```csharp
Ui.Select.Bind(() => m.Plan).Listbox[…]          // the list shows the pick; the model hears it with the next action
Ui.Select.Bind(() => m.Country).Live()[…]        // written on pick: AfterBind and a dependent select react at once
Ui.Checkbox.Bind(() => m.Agreed).Live()          // a section that shows while it is ticked
Ui.DatePicker.Bind(() => m.From).Live()          // a price that follows the date
```

- **A control of one element** — `Ui.Checkbox`, `Ui.Switch`, the native `Ui.Select` — waits in the browser,
  as an input does: ticking it sends nothing at all.
- **A control the kit draws in C#** — a listbox, a calendar, a group, a slider — still sends its own event,
  because the server draws the pick: the list shows the option, the calendar the day. What waits is the
  **write**: the model, the member's rules and `AfterBind` hear the value with the next action, in the
  order the controls were last touched, before the action's own handler. Nothing the page wrote runs per
  pick, and the page is not rendered again for one — only the control.
- **What counts as the next action here** is any handler that is not such a control drawing itself: a
  button of the page, a submit, another control's `.Live()` change, a controlled control's `OnChange`, and
  a callback the control itself raises into the page — `Ui.Otp`'s `OnComplete`, a tab's `OnClick`, a
  select's `OnInput` — which finds the model written.
- **Two controls that wait do not write each other.** A pick in the calendar leaves the listbox's pick
  waiting: a waiting control drawing itself is never an action, whichever control it is.
- **A listbox's pick is checked when it is written.** A bound `Ui.Select.Listbox` used to run its member's
  rules on submit only; they now run when the value arrives, like any bound control's.
- **Not preceded: a callback that captures no component.** The waiting picks are written ahead of a callback
  by way of the component that wrote it. A `static` lambda, or one that closes over locals only
  (`.OnComplete(_ => row.Done = true)` in a loop over rows), belongs to no component and runs without them;
  write it so that it touches the component (`_ => Finish(row)`).
- **A message under a control that waits** goes when the reader picks again, as it goes under a field at
  the first key. The pick marks the field modified at once.
- **A control a render removes takes its unwritten pick with it.** One that goes because the action itself
  replaces the page is written first. Following a link is not a handler of the page: it leaves the form,
  and a pick that was never written leaves with it — where a waiting input's text is still sent ahead of
  the navigation, because the browser holds it.
- A pick that waits lives in the session. A reconnect to a new session — a restarted server — forgets it,
  where a waiting input keeps what was typed in the browser.

A controlled input is not bound and none of this applies to it: `Input.Value(_filter).OnInput(v => _filter = v)`
hears every keystroke, as before.

### The two modes are exclusive

A control's value comes from exactly one place, and the step you open the chain with says which:

| Opened with | Mode | Its steps | Compile, but do nothing here |
| --- | --- | --- | --- |
| `.Bind(() => model.Field)` | bound | `Validate`, `AfterBind` | `Checked`, `OnInput`, `OnChange` |
| `.Value(v)` or `.Of<T>()` | controlled | `Checked`, `OnInput`, `OnChange` | `Validate`, `AfterBind` |

`Bind` and `Value` are the *openings* themselves, not steps you take later — taking one is what rules
the other out, so neither appears again on the chain. `Of<T>()` opens a controlled chain for a control
you are giving no value at all; if you want to supply one, that is `.Value(v)`.

Everything else — `Placeholder`, `Type`, `Required`, `Min`/`Max`, `OnFiles`, the whole `Class`/`Id`/
`Aria` element surface — belongs to neither and is reachable from both.

**Choosing a mode is enforced by the type**: the two openings live on the control's entry and nowhere
else, so taking one hands back the control and the other is not a member of it.

```csharp
Input.Bind(() => _model.Name).Value(_typed)   // ✗ no such step: Bind already answered this
```

The steps that FOLLOW an opening are not gated, and that is a deliberate trade. Each mode's steps used
to be declared only on that mode, which took a `Bound`/`Controlled` type argument threaded through every
generated signature to arrange — for a rule that only ever caught a property being set and then not
read. So this compiles now, and does nothing:

```csharp
Input.Bind(() => _model.Name).OnInput(v => _log = v)   // compiles; the handler never runs
```

The reason it does nothing is that bound mode already owns those: it derives the rendered value (and a
checkbox's `checked`) from the model and installs its own `oninput`/`onchange` write-back. Want a side
effect on
each bound write? That is what `AfterBind` is for.

The openings themselves stay exclusive: `Input.Bind(…)` and `Input.Value(…)` both live on the chain's
entry, so once you have taken one the other is not there to take.

Beyond the constraint/affordance attributes shared with plain HTML (`Min`/`Max`/`Step`/`Pattern`/
`MaxLength`/`MinLength`/`Multiple`/`Accept`/`List`/`Autocomplete`/`Autofocus`), the core `Input` also
carries the mobile & accessibility hints `InputMode` (on-screen keyboard), `EnterKeyHint` (action-key
label), `Spellcheck` (the enumerated `spellcheck="true|false"`), `Capture` (camera/mic for a file
input), and `DirName`. A control of your own forwards them the same way (see
[building-form-controls.md](building-form-controls.md)).

> **Fractional numbers get `step="any"` automatically.** A `decimal`/`double`/`float`/`Half` binding
> renders `<input type="number" step="any">`. Without it HTML's default is `step="1"`, so the browser's own
> constraint validation rejects `42.50` and **refuses to fire submit** — silently, with nothing thrown and
> no validation message, which reads as the form being broken. Integral types keep the implicit whole-number
> constraint. An explicit `.Step(…)` always wins, and is worth setting for money (`.Step("0.01")` makes the
> spinner step by cents).

### File inputs

`InputType.File` turns an `<input>` into a file picker. Instead of binding a value, hand it an
`OnFiles` callback (a synchronous or an asynchronous handler) that receives the selected `IRaskFile`s (`Name`/`Size`/
`ContentType`/`OpenReadStream()`), and constrain the picker with `Accept`, `Multiple`, and `Capture`:

```csharp
Input.Of<string>().Type(InputType.File).Accept("image/*").Multiple()
     .OnFiles(async files => { foreach (var f in files) await Save(f); })
```

Uploading the bytes (streaming to a server endpoint, size limits, progress) is covered end-to-end in
[http-and-files.md](http-and-files.md).

```csharp
Input.Bind(() => _model.Subscribe)   // bool     → checkbox
Input.Bind(() => _model.Age)         // int      → number
Input.Bind(() => _model.StartDate)   // DateOnly → date
Select.Bind(() => _model.Favorite)[Option.Value("Red")["Red"], Option.Value("Blue")["Blue"]]
Textarea.Bind(() => _model.Notes).Rows(3)
```

Those are the browser's own controls. An app normally writes the [UI kit](ui-kit.md)'s, which bind the same
way: `Ui.DatePicker` for a date (`.Typed()` to type it in the reader's own order as well as pick it), `Ui.TimePicker`
for a time, `Ui.DayAndTime.Bind(() => m.At)` for a date and a time in one row bound to ONE `DateTime?` (or
`DateTime`) — or `Ui.DayAndTime.Bind(() => m.Day, () => m.Time)` over a `DateOnly?` and a `TimeOnly?` the model keeps
apart, each part saying its own member's messages under itself —, `Ui.Select.Bind(…).Listbox[…]` for a choice, `Ui.Select.Bind(() => m.CompanyId).Choices(companies)` for a row of
another table (`Ui.Pillbox…Choices(…)` for several — [rows of another table](ui-kit.md#select)) and `Ui.Input` for
text and numbers.

<!-- demo:binding-multi -->

<!-- demo:binding-textarea -->

#### A choice takes only what it offered

A control that offers choices accepts a posted value only when a part of it the reader could pick, as the
latest render drew it, sends that value — compared as the render wrote it:

- a bound or controlled `Select` (and the native `Ui.Select`), and `Select.OnSelect`'s raw values: an enabled
  `<option>` written under it or under an `<optgroup>`;
- a `Ui.RadioGroup` in every variant (list, `.Segmented`, `.Cards`, `.Pills`, `.Buttons`): an enabled `Ui.Radio`,
  which sends its own `Value`;
- a `Ui.CheckboxGroup`'s `Ui.Checkbox`, and a `Ui.Toggle`: only while it is enabled;
- a `Ui.Tabs` (bound or with `OnChange`): the `Name` of a `Ui.Tab` it drew enabled;
- a form control drawn disabled — a `Button`, `Input` (its `OnFiles` too), `Select` or `Textarea` with
  `.Disabled(true)` or a `disabled` written through `Attributes`, a `Ui.Button.Disabled()`, or any of them inside a
  `Fieldset.Disabled(true)` outside its first `Legend` — takes no event at all: every handler it registers is refused
  in one place in Core, a click as much as a value, bound or controlled, an empty selection included. A browser sends
  no event to a disabled control, so whatever arrives was written by hand. The markup is an enabled control's, and
  every kit control that draws one (a standalone `Ui.Checkbox`, `Ui.Switch`, `Ui.Input`, `Ui.Textarea`, `Ui.Phone`,
  the native `Ui.Select`, a `Ui.Slider` thumb, a `Ui.Otp` cell, …) inherits it — the warning names the kit control,
  `Ui.Slider 'Volume'`. Its rules still run when the form is submitted;
- a kit control that writes through a hidden field or an engine of its own, drawn `.Disabled()`: nothing —
  `Ui.DatePicker` (`.Range`, typed or not), `Ui.DatetimePicker`, `Ui.TimePicker` (list and typed),
  `Ui.ColorPicker`, `Ui.Select.Listbox`/`.Combobox` (a pick, its clear, its create row), `Ui.Pillbox` and
  `Ui.Editor`;
- a `Ui.Calendar`, `Ui.DatePicker` and `Ui.DatetimePicker`, from their calendar: only days it drew the reader able
  to press — within `Min` and `Max`, not `Unavailable` — or a preset range exactly as it spans today;
- a held `Ui.Select.Listbox`/`.Combobox` and `Ui.Pillbox`: an enabled row's value, or an answer it still shows. Sent
  nothing else, it keeps its answer.

Anything else (an id the reader was never shown, a disabled choice) is refused: the model keeps its value, no
rule runs, `AfterBind` and `OnChange` are not called, and a warning that names the control — never the value —
goes to the `Rask.Forms` log. `Select.OnSelect` drops the values no option offered and is not called for a
selection of nothing else. It is not an authorization check: the choices you draw are the ones you decided
this reader may pick. The menu's radios and checkboxes keep the same rule.

A date, range or time the READER typed outside a picker's limits is not taken either, but it is the reader's, so
it is said rather than logged: a date typed into a `Ui.DatePicker`'s own field before `Min`, after `Max` or
`Unavailable`; a range (`Ui.DatePicker.Range`, `Ui.DatetimePicker.Range`, `Ui.Calendar.Range`) shorter than
`MinRange` or longer than `MaxRange`, typed or announced; a time typed into a `Ui.TimePicker` before `Min`, after
`Max` or in an `Unavailable` stretch; a `Ui.DatetimePicker`'s time outside `MinTime`/`MaxTime`, before `Min` or after
`Max` on their own day, or in an unavailable slot. The model keeps its value, and in a form the field says why
under it — "Choose a date on or after Mar 1, 2026.", "Choose a range of at most 7 days.", "Choose a time at or
after 9:00 AM.", "Mar 13, 2026 is not available." — and the save is refused until a value within the limits
arrives, exactly as for a date typed half way (`RaskString.ValidationDateMin`/`ValidationDateMax`/
`ValidationUnavailable`/`ValidationRangeMin`/`ValidationRangeMax`/`ValidationTimeMin`/`ValidationTimeMax`,
Hungarian in Rask.Ui). A controlled picker's `OnChange` is not called. Server-side, the same in Server and WASM:
write no rule for the limits you already gave the picker.

### Empty value handling

When the user clears an input, `BindingHelpers.TrySetTyped` decides what the empty string maps to:

| Property kind | Empty input becomes |
|---|---|
| `Nullable<T>` value type (`int?`, `DateOnly?`, …) | `null` |
| NRT-nullable reference type (`string?`) | `null` (detected via `NullabilityInfoContext`) |
| Non-nullable value type (`int`, `DateOnly`, enum) | `default(T)` — so a number/date/enum input is clearable |
| Non-nullable `string` | `""` (verbatim) |

A value that fails to parse (`"1.5"` or `"99999999999"` into an `int`, a date half typed) leaves the model
unchanged **and is not accepted in its place**: the field says what it could not read ("Enter a whole
number."), ahead of "required" and of any rule, and the submit handler does not run until a value arrives
that fits — see [what a number field says when it cannot read what was typed](forms-validation.md#what-a-number-field-says-when-it-cannot-read-what-was-typed).

**Controls over a value type bind its nullable too.** `Ui.Checkbox` and `Ui.Switch` are over `bool`,
`Ui.Slider` over a number and `Ui.Calendar` over `DateOnly`, and each also binds the nullable
form: `Ui.Checkbox.Bind(() => model.InStock)` compiles whether `InStock` is a `bool` or a `bool?`, which is what a
[generated form model](domain.md#a-create-and-an-edit-form) holds. A `null` draws as the control's empty state
(unchecked, no day picked, a slider at its lowest value), and a change writes a value. `Value` and `OnChange` stay over the plain type. The
date controls go one further: `Ui.Calendar` binds a `DateOnly`, and behind Flux's mode steps a collection of days
(`Ui.Calendar.Multiple.Bind(…)`) or a `UiDateRange` (`Ui.Calendar.Range.Bind(…)`); `Ui.DatePicker` a `DateOnly` or,
as `Ui.DatePicker.Range`, a `UiDateRange`; `Ui.TimePicker` a `TimeOnly?`, a `TimeOnly` or a collection of
times; and `Ui.DayAndTime` a `DateTime?` or a `DateTime` — whose day may be emptied only when the member is a
`DateTime?` the model does not require — or a `DateOnly?` and a `TimeOnly?` as two members
([UI kit](ui-kit.md#a-day-and-a-time)).

Every BCL [`IParsable<T>`](https://learn.microsoft.com/dotnet/api/system.iparsable-1) type (numbers,
`Guid`, `DateOnly`/`DateTime`/`TimeOnly`, `bool`, …) binds with no setup, and so does a **custom**
`IParsable<T>` value type under the default interpreter build. For a full [WASM AOT](aot.md) build,
register your custom form-field types once at startup with `RaskBinding.RegisterParsable<Money>()` —
custom route/query param types are registered automatically by the generator.

<!-- demo:binding-nullable -->

<!-- demo:binding-clear-default -->

### Binding lifecycle

Each change handler runs in order: write the value, `NotifyFieldChanged`, run `AfterBind`
(if supplied, only when a write actually happened), `NotifyFieldTouched`, then re-validate the field.
When that happens is the control's [bind timing](#bind-timing): with the next action unless the chain
says `.Live()`, `.Debounce(…)` or `.Blur()`. A text field sent at every keystroke
(`.Debounce(TimeSpan.Zero)`) stays quiet until it is touched, then re-validates on every keystroke.

`AfterBind` fires **after** the new value is written and before validators run — handy for dependent
fields (pick a country, repopulate the city dropdown in the same render):

```csharp
Select.Bind(() => _model.Country)
    .AfterBind(c => { _cities = Cities[c]; _model.City = _cities[0]; })[ /* options */ ]
Select.Bind(() => _model.City)[_cities.Select(c => Option.Value(c)[c])]
```

<!-- demo:binding-afterbind -->

It takes an asynchronous handler just as readily — one property, either shape — and awaits before the
post-handler render, so a dependent async lookup (repopulate a dropdown from an API) surfaces its
loading state on its own, with no manual `StateHasChanged()`:

<!-- demo:binding-afterbind-async -->

### What a bind costs, and the one case worth changing

`Bind(() => …)` takes an `Expression<Func<T>>`, and **the C# compiler builds that tree at the call
site on every render**. Building it resolves a member token on the bound property's *declaring type*,
and that cost scales with how many members the type has:

| what you bind | B/render |
| --- | --- |
| `Input.Value(…)` — controlled, no bind at all | 1216 |
| `Input.Bind(() => Model.Name)` — a plain model | 3041 |
| `Input.Bind(() => Draft)` — a property on the **component** | 5011 |

The third row is the surprise, and it is invisible at the call site: the two spellings look equally
cheap. A component is a markup host, so the generator injects the chain entries into it — around 430
members — and binding a property *declared there* pays for resolving a token on a type that large.
Binding the same value on a plain model does not.

**For nearly every app this does not matter.** A form re-renders on interaction, not in a loop; a few
kilobytes per render is well under the noise. Reach for the fix below only when a bound control is in
something genuinely hot — a virtualized grid, a control that re-renders on every keystroke of a large
document.

**The fix is at the call site: hoist the expression into a field**, so it is built once instead of
per render.

```csharp
using System.Linq.Expressions;

public sealed partial class Editor : Component
{
    private readonly Model _model = new();

    // Built once, in the constructor, rather than on every render.
    private readonly Expression<Func<string>> _name;

    public Editor() => _name = () => _model.Name;

    protected override Component Render() => Input.Bind(_name);
}
```

The two rows converge once the tree stops being rebuilt: **2721 B/render** hoisted against a plain
model, **2753 B** hoisted against a property on the component — so hoisting does not merely help the
expensive shape, it erases the difference. What remains is the binding machinery (`FieldIdentifier`,
validator registration, owner tracking), which is shared with the plain-model case.

Every number on this page is pinned by `BuilderEntryAllocationPinTests`, so none of them can drift
without a test going red.

The other move is simply **to bind a plain model rather than a property on the component**
(`() => _model.Name`, not `() => Draft`), which is what most code does already and costs 3041 B rather
than 5011 B without any ceremony.

> Why not fix this in the framework? Measured and ruled out in
> [#803](https://github.com/pal-tamas/rask/issues/803): a Roslyn interceptor cannot help — an
> interceptor must keep the intercepted method's signature (CS9144), and the lambda is converted to an
> expression tree when the call is *bound*, before interception applies. Adding a `Func<T>` overload
> does not help either, because C# prefers the `Expression` one. What remains is making the injected
> chain entries inheritable instead, which is a change to how every markup host is generated.

---

## 2. `Form.Model(…)` and the `EditContext`

`Form.Model(model)` wraps the inputs and owns an `EditContext` — the per-field state store plus the
validator pipeline. Bound inputs inside the form discover that context automatically.

```csharp
Form.Model(_model).OnSubmit(m => Console.WriteLine(m.Username))[
    Input.Bind(() => _model.Username),
    Button.Type(ButtonType.Submit)["Sign up"]
]
```

A field's rule is a step on its input — the value in, the messages that reject it out:

```csharp
Input.Bind(() => _model.Username)
    .Validate(v => v.Length >= 3 ? [] : ["At least 3 characters."])
```

A rule across fields goes on the form the same way, and either can be `async`: see
[validation](forms-validation.md).

Submit runs every rule, marks every registered field touched, then routes:

- valid → `OnSubmit` (or, if unset, `OnAnySubmit` with the raw `FormData`),
- invalid → `OnInvalidSubmit`.

`OnSubmit` / `OnInvalidSubmit` are one `Callback<TModel>` each, so either takes a synchronous
(`Action<TModel>`) or an asynchronous (`Func<TModel, Task>`) handler — a bare lambda or a method group,
no cast, no `…Async` twin.

While a submit is in flight the form takes no second one, as Livewire's `wire:submit` does: at the press,
before anything is sent, the runtime disables the form's submit buttons (a `<button form="…">` outside it
too) and makes its text fields `readonly`, and it gives them back when the handler and the render it caused
are done — on the Server host with that submit's ack, in a WebAssembly app when the dispatch returns. A
double click, a second Enter or a script's `requestSubmit()` meanwhile is dropped in the browser and never
sent, so a non-repeatable save (an upload, a payment) runs once. The fields stay in what is sent:
`readonly`, unlike `disabled`, keeps a value in the form data. A render meanwhile does not hand the
controls back early, and what it says of them (`.Disabled(f.Submitting)` below) is what they are left with —
except a field it shows a message under: a submit whose rule awaits a lookup draws what it already knows at
that await, and the field it calls wrong is handed back then, so the first key of the correction is heard.
The button's spinner still waits its 200 ms, so a fast save never flashes one. A plain button's `OnClick`
is not held: Livewire disables nothing for `wire:click`.

### Children that follow the submit

Children are normally a fixed list. Give the form a **function** instead and it is called on every
render with the state of its submit — `f.Submitting` while one is in flight, `f.Error` for what the last one
threw — so the markup can say so without the page tracking it:

```csharp
Form.Model(_model).OnSubmit(Save)[f => [
    Input.Bind(() => _model.Username).Disabled(f.Submitting),
    Button.Type(ButtonType.Submit).Disabled(f.Submitting)[f.Submitting ? "Saving…" : "Sign up"]
]]
```

The flag is true from the moment the submit handler starts until it returns — including when it
throws — and the form re-renders on both edges. Only an `async` handler can be observed in that
state: a synchronous one returns before there is a frame to paint.

<!-- demo:form-submit-state -->

A handler that throws tells the reader by itself: an error toast, *The save failed. Please try again.*,
in the reader's language (`RaskString.FormSaveFailed`). Their values stay in the fields and the submit is
free for another try. A form that draws its own messages with `Validation.Summary` says the sentence
there and raises no toast. The exception's own text is never shown — a server app in Development adds
its type and message — and it is logged as an error. The same value's `Error` holds it
(`f => [ f.Error is null ? null : … ]`), for a page that wants to draw more than the toast.

To handle it yourself, catch it in the handler. What the handler catches never reaches the form, so
there is no toast. A handler outside a form that throws gets a sentence of its own
([a handler that fails](composition-lists.md#a-handler-that-fails)):

```csharp
async Task Save(SignUp model)
{
    try { await Accounts.Create(model); }
    catch (MailServerDownException) { Toast.Warning("Signed up. The welcome mail will follow."); }
}
```

A handler that names the fields it is about — the store refusing a duplicate — is shown
[under those fields](forms-validation.md#the-database-said-no) and is not an error at all; a
DataAnnotations `ValidationException` is treated the same way. A refusal that names no field shows its
own sentence — in `Validation.Summary`, or as the error toast when the form has none — in place of
*The save failed*. A cancelled save says nothing. See
[a save that fails](forms-validation.md#a-save-that-fails).

The fixed-list forms are untouched and still bind exactly as before:

```csharp
Form.Model(_model)[Input.Bind(() => _model.Username), Button.Type(ButtonType.Submit)["Sign up"]]
```

Only a form offers the function form. It is an indexer declared on `Form` itself, so
`Div[f => …]` does not compile — there is no submit state behind a `<div>` to report. See
[`Form`](../src/Rask.Core/Dom/HTMLFormElement.cs).

### Asking before an action

`.Confirm("…")` asks the reader before a click or a submit is sent — in the browser's own `confirm` dialog, or
in [`Ui.Confirm`](#ask-for-a-confirmation-in-a-dialog-of-your-own) when the layout has one. Livewire's
`wire:confirm`. It is a step on every element, so it works on a plain `Button`, a kit `Ui.Button`, a `Form` and
a submit button alike, and on a `Ui.MenuItem` too, which puts it on the row it draws:

```csharp
Ui.Button.Icon(Ui.IconName.Trash).OnClick(() => Delete(row)).Confirm("Delete the partner?")

// Livewire's wire:confirm.prompt: the reader types the name, and only an exact match goes (case and all).
Ui.Button.Danger.OnClick(() => Delete(row)).Confirm("Type the partner's name to delete it", expect: row.Name)

// On a form it asks before the submit; on a submit button, before the submit that button makes.
Form.Model(_order).OnSubmit(Send).Confirm("Send the order?")[ … ]

// A row's ⋯ menu: the pick asks first.
Ui.MenuItem.Danger.Icon(Ui.IconName.Trash).OnClick(() => Delete(row)).Confirm("Delete the partner?")["Delete"]

// A link asks too, and a yes follows it: a string href as the browser follows any link (a GET, a page load).
Ui.MenuItem.Href("/logout").Confirm("Sign out of Acme?", heading: "Sign out")["Sign out"]

// The sign-out row in the catalog's words: "Are you sure you want to sign out?" under "Sign out" —
// "Biztos ki szeretne jelentkezni?" under "Kijelentkezés" in Hungarian — named and iconed the same.
Ui.MenuItem.Href(SignOut.Path).SignOut()
```

<!-- demo:form-confirm -->

Every way of asking, live, with the dialog's steps: [Confirm on the UI tab](https://rask.sh/ui/confirm/).

The question is asked in the browser, before anything leaves it, so it costs no round trip. Cancelled, **nothing
is sent**: not the click or the submit, and not the fields still waiting to go with it — a bound field that
waits for the next action keeps waiting. Both hosts ask the same way. The element carries the question as
`data-rask-confirm` (and the expected answer as `data-rask-confirm-expect`), so it is in the page's own words
and in its language. Do not put a secret in `expect:` — it is in the page.

#### Ask for a confirmation in a dialog of your own

The browser's `confirm` and `prompt` cannot be styled or translated. Put `Ui.Confirm` in the layout, once, and
every `.Confirm("…")` under it asks in that dialog instead — a [`Ui.Modal`](ui-kit.md#who-owns-the-state) drawn
as Flux's confirmation: a short heading, the call site's question as the sentence beneath it, No and Yes.

```csharp
Ui.Main[Outlet],
Ui.Toast,
Ui.ConfirmLeave,
Ui.Confirm.Heading("Megerősítés").No("Nem").Yes("Igen")   // "Confirm", "No" and "Yes" when unset
```

The call sites do not change. No, the close button, Escape and a press outside call the action off; only Yes
lets it go — the same press, or the same submit by the same button, and a form is held only then. The page may
render while the reader is asked, as often as it likes — a board told of every change: yes presses the same
button as the page stands then (the element pressed, or the one drawn under its `Key` in its place), and a button
that is gone, or another action drawn in its place, runs nothing. With
`expect:` the dialog shows a field, and Yes waits until the answer is typed exactly, case and all; Enter in the
field is Yes once it matches. It all happens in the browser: opening the dialog, typing in it and closing it
send the server nothing, and focus goes back to the control that was pressed. A question can bring its own title,
which replaces the layout's for that question alone:

```csharp
Ui.Button.Danger.OnClick(() => Delete(row)).Confirm("Biztosan törli a céget?", heading: "Cég törlése")["Törlés"]
```

Without `Ui.Confirm` the browser asks, as before, and `heading:` is not shown. The words default from the [string catalog](localization.md) (`ConfirmHeading`, `ConfirmNo`,
`ConfirmYes`). `Ui.Confirm.Primary()` draws Yes as the primary button, for questions that lose nothing.

A question that only a check in the handler can raise is asked from code, in the same dialog, with
`await Ui.Confirm.Ask("…")` — true for Yes, false for every other way it closes ([the kit](ui-kit.md)):

```csharp
if (await Periods.Overlapping(_period) && !await Ui.Confirm.Ask("The period overlaps another. Save it anyway?"))
{
    return;
}
```

### Ask before leaving unsaved changes

`ConfirmLeave` makes a form ask before the reader walks away from what they typed:

```csharp
Form.Model(_model).OnSubmit(Save)
    .ConfirmLeave("Leave without saving your changes?")[
    Input.Bind(() => _model.Name),
    Button.Type(ButtonType.Submit)["Save"]
]
```

With no question of its own, `.ConfirmLeave()` asks the framework's — *Leave without saving?*, and
*Biztos elhagyja mentés nélkül az oldalt?* in Hungarian, from the
[strings catalog](localization.md#translating-the-frameworks-own-text) (`ConfirmLeaveQuestion`):

```csharp
Form.Model(_model).OnSubmit(Save).ConfirmLeave()[ … ]
```

<!-- demo:form-confirm-leave -->

The dialog it asks in, live, with its steps: [Confirm leave on the UI tab](https://rask.sh/ui/confirm-leave/).

The form counts as **unsaved from the first thing typed or changed in it**. That is decided in the
browser, so it holds for a value that has not reached the server yet — and it is not a comparison with
the original values: typing a value and typing it back still counts. While it is unsaved, every way out
of the page asks first:

| The reader… | What asks |
| --- | --- |
| follows a link inside the app (`NavLink`, a Markdown link to another page) | your message, in the browser's own `confirm` dialog — or in [`Ui.ConfirmLeave`](#ask-in-a-dialog-of-your-own) when the layout has one |
| is navigated by script (`__raskHost.navigate(…)`) | the same dialog |
| presses Back or Forward | the same dialog; staying puts the address and the history back where they were |
| closes the tab, reloads, or follows a link out of the app | the browser's own "leave site?" prompt. No page can set its text, so your message is not shown there. |
| closes the [`Ui.Modal`](ui-kit.md#who-owns-the-state) the form is in — its close button, a `Ui.ModalClose`, Escape, a press outside | the same dialog; staying keeps the modal open with what was typed |

Staying sends nothing to the server and leaves the page exactly as it was, typed values included.

The form is **clean again after a submit that passed validation and whose handler returned** — and
unsaved again at the next edit. A submit validation refuses, or a handler that throws, leaves it
unsaved. A navigation your own handler makes never asks, so the usual "save, then go to the list" needs
nothing extra:

```csharp
private async Task Save(Product product)
{
    await product.Save();
    Routes.ProductsPage().AsLeft().Go();   // not asked about: the app is leaving, not the reader
}
```

`.AsLeft()` (Rask.Ui) is the list as the reader left it this session — its filters, sort, page and search, an editor
dialog's key left out — so a save returns there rather than to the bare list; a list the session has not shown is the
route as given ([ui-kit.md](ui-kit.md#a-list-page)).

#### Ask in a dialog of your own

Put `Ui.ConfirmLeave` in the layout, once, and every guarded form under it asks in that dialog instead of the
browser's `confirm` — a [`Ui.Modal`](ui-kit.md#who-owns-the-state) drawn as `Ui.Confirm` is, with the form's
message as its heading:

```csharp
Ui.Main[Outlet],
Ui.Toast,
Ui.ConfirmLeave.Stay("Nem").Leave("Igen")   // the two buttons; "Stay" and "Leave" when unset
Ui.ConfirmLeave.Heading("Unsaved changes")  // a short title over the form's question, as Flux titles a confirmation
```

The forms do not change. `Stay`, the close button, Escape and a press outside the dialog all keep the reader
on the page, as it was; `Leave` carries on with the navigation that was asked for — the same link, the same
`navigate(…)`, the same history entry Back was going to. The dialog is rendered closed and opened in the
browser, so it needs no round trip to appear. Closing the tab, a reload and a link out of the app still get
the browser's own prompt: no page can draw that one.

A form that is no longer on the page guards nothing. With several guarded forms on one page, the first
unsaved one asks, with its own message.

#### A form in a modal

A create or edit form in a `Ui.Modal` or a flyout is guarded the same way, with nothing added: closing the
modal is leaving the form, so every way the **reader** closes it asks first. `Leave` closes it — `OnCancel`
and `OnClose` run as they would have — and what it held is let go; a form in a closed modal guards nothing.
Escape is asked about only once nothing inside the modal took it, so a listbox open over the form closes first.
A close the page makes is not asked about, as a `Go.To(…)` is not: the usual "save, then close" is

```csharp
private async Task Save(Product product)
{
    await product.Save();
    _editing = null;   // Ui.Modal.Open(_editing is not null) closes, unasked
}
```

A cancel button of your own should close the modal in the browser — `Ui.ModalClose[Ui.Button["Mégse"]]` —
so it asks too; one whose `OnClick` sets the field closes it from the page, unasked.

Three things to know:

- **What counts as an edit** is the browser's `input` and `change` events inside the form, and every value
  the reader chooses in a kit control that draws itself — a listbox, a combobox or a pillbox option, a pill
  taken away, a day in a `Ui.Calendar` or a `Ui.DatePicker`, a time, a colour, a rating, a picker's clear
  button, a `Ui.FileItemRemove`. The browser raises no event for those, so the page that hears the press
  says so on the form, bound with `Bind` or held with `Value`/`OnChange` alike, written to the model yet or
  not. Opening a list, moving through a calendar and typing in a list's search field change nothing and
  count for nothing. A value the page writes by itself is no edit, and neither is a button of your own
  whose `OnClick` sets a property.
- **A pick is counted once the page has heard it**, which is a round trip after the press. A reader who
  picks and follows a link within that instant is not asked.
- **Which way Back went** is read from the Navigation API. On a browser without it (Safari before 26.2,
  Firefox before 147) a refused Back or Forward still leaves the reader on the page with the right
  address, but as a new history entry — and Back and Forward ask with `confirm` there even when the
  layout has a `Ui.ConfirmLeave`, because the move cannot be made a second time.
- **A press in the first instant** — before the script that guards the form has arrived — is not asked
  about. The guard is one of the [behaviour hooks](js-interop-runtime.md#how-the-hooks-load): a page
  rendered with the form loads it with the runtime, so this is a matter of milliseconds.

### Auto-created vs explicit `Context`

By default the form creates (and caches per model reference) its own `EditContext` — it persists
across renders of the same model, so field state survives re-renders. Pass `.Context(…)` to own the
instance yourself when you need to drive validation imperatively, register an
`IAsyncFieldValidator`, or tune `ValidatingStickyMs`:

```csharp
_ctx = new EditContext(_model);
_ctx.AddValidator(new SlowTitleValidator());

Form.Model(_model).OnSubmit(m => _submission = "Saved").Context(_ctx)[
    Input.Bind(() => _model.Title),
    Button.Type(ButtonType.Button).OnClick(() => _ctx.Validate().AsTask())["Validate now"],
    Button.Type(ButtonType.Submit).Disabled(_ctx.IsValidatingAny)["Save"]
]
```

`Form` opens with `Model`, its one required step; `Context` is an optional step after it.

### Binding a data model

The form that changes an aggregate is the [form over its operation](#a-form-over-an-operation). An aggregate with no
steps of its own — a category, a setting — may name the `Model` trait instead, and gets a form model generated beside
it: `CategoryModel` beside `Category`, every mapped property settable, its value objects' rules and unique indexes
asked by the form with nothing declared here:

```csharp
private readonly CategoryModel _category = new();

Form.Model(_category).OnSubmit(c => Category.Create(c))[
    Ui.Field.Bind(() => _category.Name),
    Ui.Button.Primary.Submit["Create"]
]
```

The model is what the generated writes take — `Category.Create(model)`, `Category.Update(id, model)` — and each asks
the aggregate's rules again on the server before anything is written. It carries no id, so the row an edit saves is
the one the page's own route names, and it carries the row's `Version` back, so `Update` refuses a save that lost a
race rather than overwriting the other one — a `RaskValidationException` about the whole form, shown at its top. See
[a create and an edit form](domain.md#a-create-and-an-edit-form).

### An edit page that reads its record

The **page** reads the record, in `OnUpdated` (it runs again when the route's id changes) or in `OnMount`, and
draws the form only once the record is there:

```csharp
[Route("/trailers/{Id}")]
public sealed partial class TrailerEditPage : Component
{
    private TrailerModel? _trailer;
    private bool _read;

    [RouteParam] public int Id { get; set; }

    protected override async Task OnUpdated()
    {
        _trailer = await Trailer.Model(Id);
        _read = true;
    }

    protected override Component? Render() =>
        _trailer is not null ? TrailerForm.Key(Id).Model(_trailer)
        : _read ? Ui.Callout["No such trailer"]
        : Ui.Text["Loading…"];
}
```

The first `GET` waits for the read, so its document is the form with its record. A navigation in place
[sends the page at once](routing.md#a-page-reached-in-place-is-sent-at-once), so the reader sees the
loading branch first and the form when the read lands — the page is drawn again when its hook's `await`
completes, with nothing to call. The `_read` flag is what keeps "No such trailer" for a trailer that does not
exist: without it, every edit page reached by a link would say so until its record arrived.

A read that **throws** shows the [built-in error page](lifecycle.md#gotcha-a-faulted-async-hook-takes-the-page-not-the-component)
in place of the page (the first `GET` answers 500). To say a sentence of your own instead, wrap the read in
`try`/`catch` and draw it from `Render`.

A form is its **model's** form: `Form.Model(model)` keeps one `EditContext` per model instance, so handing it
another instance — the next record under the same `Key`, a record read again — is a new form with that
instance's values, and nothing typed into the previous one is left in its fields.

**A form component reads its model where it draws the form.** A component of your own that wraps the form takes
the model as a prop and uses it in `Render`:

```csharp
public sealed partial class TrailerForm : Component
{
    public TrailerModel? Model { get; set; }

    protected override Component? Render() =>
        Model is null ? null : Form.Model(Model).OnSubmit(Save)[Ui.Input.Bind(() => Model.Name).Label("Name")];
}
```

The trap is a copy: `_model = Model;` in the component's `OnMount` is taken **once**, when the component is
mounted. If the page mounted it before its read was back — `TrailerForm.Model(_trailer)` with no
`null` check around it — the copy is `null` and stays `null` however many times the page hands it the record
afterwards. Read the prop in `Render`; where a copy is really wanted (a draft the reader may discard), take it
in `OnUpdated`, which runs again whenever the prop changes, or mount the component only once the model is read,
as the page above does.

### Rendering messages

Two headless components read the context — both take a required `Template` step, written first, so you own the markup,
and both render nothing when there's nothing to show:

```csharp
Validation.Message.Template(errs => Div.Class("field-error")[errs[0]]).For(() => _model.Email)

Validation.Summary.Template(entries => Ul[entries.Select(e => Li[Strong[e.Field], ": ", e.Message])])
```

`Validation.Message.For` keys a single field; `Validation.Summary` lists every `ValidationEntry`
(`Field` + `Message`), with form-level messages carrying an empty `Field`.

<!-- demo:validation-summary -->

**A long form in tabs.** `Ui.FormTabs` inside the `Form` reads the same context: each tab counts the invalid fields it
`Holds(…)` in a red badge, and every refused submit — the rules failed, or the save threw field failures — opens the
first tab holding one. Nothing is handed to it:

```csharp
Form.Model(m).OnSubmit(Save)[
    Ui.FormTabs[
        Ui.FormTab.Name("details").Label("Details").Holds(() => m.Name, () => m.Email)[ /* fields */ ],
        Ui.FormTab.Name("address").Label("Address").Holds(() => m.Address)[ /* every field on the address */ ]
    ],
    Ui.Button.Submit["Save"]]
```

See [form tabs in the UI kit](ui-kit.md#a-long-forms-tabs).

**Focus goes to the first mistake.** After every refused submit — the rules failed, or the save threw field
failures — focus moves to the first field marked invalid, in document order, and it is scrolled into view; inside
`Ui.FormTabs`, once the tab holding it is shown. A submit that went through moves nothing, and a reader who moved
focus elsewhere while the save was in flight is left there. Both hosts, nothing to wire — see
[accessibility](accessibility.md#a-refused-submit-takes-the-reader-to-the-first-mistake).

### Controls at a glance

Every input works in two shapes — **controlled** (`Value` + `OnChange`, the parent owns the value) and
**bound** (`.Bind(() => model.X)`, two-way). A readout rendered *outside* the control follows a
controlled one at every change, and a bound one that says `.Live()`. The matrix below covers text, textarea and select; the [UI kit](ui-kit.md)'s controls
take the same two shapes, since they implement the same `IFormControl<T>`.

<!-- demo:form-controls-input -->

<!-- demo:form-controls-textarea -->

<!-- demo:form-controls-select -->

**Labels.** `Ui.Input`, `Ui.Textarea` and `Ui.Select` are Flux UI's: `Label` draws a label over the field,
`Description` help text under the label, and each bound field shows its own validation message under the control.
As in Flux, a control draws a label only when it is given one: a bare bound control is never labelled by its
member. The member's `[Display(Name = …)]` — the one attribute Rask names a field by (`[DisplayName]` is read nowhere,
[RASK131](diagnostics.md#rask131)), so the label, the validation message's `{0}` and a `Ui.Filters` badge all say the
same name — labels it from a FIELD: a `Ui.Field` around the control with no `Ui.Label` of its own, or `Ui.Field.Bind`:

```csharp
public sealed class SignUp
{
    [Display(Name = "Work email")] public string Email { get; set; } = "";
}

Form.Model(_model)[Ui.Field[Ui.Input.Bind(() => _model.Email), Ui.Error]]   // <label>Work email</label> over the input
Form.Model(_model)[Ui.Input.Bind(() => _model.Email)]                       // neither label nor message
```

A bare control draws no message either. In a table cell, whose column header names it, write the message beside
it as Flux's `<flux:error name>` sits beside its input: `Ui.Error.For(…)` shows that member's messages, each row its
own, and the control is `aria-invalid` while its member holds one:

```csharp
Ui.TableCell[Ui.Input.Bind(() => row.Size).Type(InputType.Number), Ui.Error.For(() => row.Size)]
```

**The whole field from the member.** `Ui.Field.Bind(() => m.X)` picks the control as well: its label, help
text and placeholder are the member's `[Display(Name, Description, Prompt)]`, its badge the automatic
*Required*, and the control what the member holds — its [field shape](forms-validation.md) on a generated
model, else its type. Text is a `Ui.Input` (`email`/`tel`/`url`/`password` from `[EmailAddress]`, `[Phone]`,
`[Url]`, `[DataType(DataType.Password)]`; its `maxlength`/`minlength` from `[StringLength]`, `[MaxLength]`,
`[MinLength]`, `[Length]`; drawn in capitals for a value object declared `Upper: true`; `.Multiline()` or
`[DataType(DataType.MultilineText)]` a `Ui.Textarea`), a `DateTime` its shape says is a day (`[DataType(DataType.Date)]`,
a `FieldKind.Day` shape) a `Ui.DatePicker` alone — a day picked keeps the time of day the value had, a first one is
midnight —
`[UIHint(FieldHints.Switch)]` on a `bool` a `Ui.Switch` and `[UIHint(FieldHints.RadioGroup)]` on an enum a
`Ui.RadioGroup` ([what a member's attributes tell its control](forms-validation.md)), a number an input with its
`min`/`max` and a step of its scale, a day a `Ui.DatePicker` (ending today when it may not be after it), a moment
a `Ui.DayAndTime`, a `bool` a `Ui.Checkbox`, an enum a `Ui.Select` over its members in their order (an empty
choice first for a nullable one), and a range two controls under one label, each end with its own message.
Another row's id is a select over the rows you give, in the shapes `Ui.Select.Choices` takes —
`.Choices(Vehicle.Find)`; without them rendering throws and says so. `[Editable(false)]` makes a text or
number input read-only and any other control disabled. Each `Label`, `Description`, `Placeholder` or `Badge`
step wins over the attribute; `Live` reaches every control, `Blur` and `Debounce` the text ones.

To choose the control and tune it, bind the control and put it in a `Ui.Field` with no `Ui.Label` of its own: the
field labels it from the member — the same `[Display(Name)]`, the automatic *Required* badge, and the
`[Display(Description)]` unless the field holds a `Ui.Description`. So there are three ways to write a field:

```csharp
Ui.Field.Bind(() => _model.Email)                                    // the field picks the control: label, email input, message
Ui.Field.Bind(() => _model.Km)                                       // a range: Km.Start and Km.End
Ui.Field.Bind(() => _model.Vehicle).Choices(Vehicle.Find)            // a typed id
Ui.Field.Bind(() => _model.Note).Multiline()
Ui.Field[Ui.Input.Bind(() => _model.Plate).InputClass("uppercase"), Ui.Error]   // you pick and tune; the member labels it
Ui.Field[Ui.Label["Plate"], Ui.Input.Bind(() => _model.Plate), Ui.Error]        // fully by hand, drawn as written
```

<!-- demo:floating-labels -->

### Accessible validation

A control of your own (see [building form controls](building-form-controls.md)),
and the [UI kit](ui-kit.md)'s controls) expose validation to assistive tech automatically — no extra props.
When a bound field has messages, the control renders `aria-invalid="true"` and an `aria-describedby` that
points at the error message's `id` (and the description's `id` when `.Description(…)` is set), so a screen
reader reads the error with the field rather than detached from it. Valid fields with a `Description` still get
`aria-describedby` to it. Two fields on one page that bind the same member name do not share an id: the
second `Name` is `f-name-2`, with its own label, error and description ([field ids](ui-kit.md#fields-label-description-error)).

**Composing the field yourself.** The kit's [`Ui.Field`](ui-kit.md#fields-label-description-error) is the
same three parts as components — Flux UI's field — for when the label, the help text or the message needs
to sit somewhere a control's own `Label` would not put it:

```csharp
Ui.Field[
    Ui.Label.Badge("Required")["Email"],
    Ui.Input.Bind(() => _model.Email).ShowValidation(false),
    Ui.Error,                                   // or Ui.Error.For(() => _model.Email) outside a field
    Ui.Description["We only write about your order."]
]
```

`Ui.Label` is a `<label for>` pointing at the control, and `Ui.Error` is a `role="alert"` live region that
is always in the page and shows the first message the form holds for the bound member.
`Ui.Fieldset.Legend("Shipping address")[…]` groups several fields under one heading.

A combobox control — [`Ui.Select`](ui-kit.md#select) with `.Listbox` or `.Combobox`, over one answer or many —
carries `role="combobox"` on a real `<button>` or text `<input>`. Both are labelable elements, so the
select's `Label` names it through the field's `<label for>`, as it does the native `<select>`. Alongside
it goes the popup contract: `aria-haspopup="listbox"`, `aria-expanded`, `aria-controls` naming the
list, and `aria-activedescendant` naming the option the keyboard cursor is on while focus stays on the
box. Options are `role="option"` carrying `aria-selected`, and an unavailable one carries
`aria-disabled` — **present only when it is true**, since a valueless `aria-disabled` reads as `true`
and would mark every option unavailable.

A list that takes more than one answer says so on the listbox itself, with
`aria-multiselectable="true"`. Without it a reader meets options that each carry `aria-selected`
and has no way to learn that a second one is allowed — the options look identical either way, so
the fact that several may be chosen lives nowhere else.

If you build your own control from the core `Input`/`Validation.Message` primitives ([building form controls](building-form-controls.md)), mirror the same
three attributes so the field stays accessible: `aria-invalid` on the control, `aria-describedby` from
the control to the message `id`, and `role="alert"` on the message container. See
[accessibility.md](accessibility.md#form-validation).

## A form over an operation

An operation of a `partial` aggregate ([an operation, run as one write](domain.md#an-operation-run-as-one-write)) is a
form of its own, its fields its parameters — no model class to write, no rule to repeat:

```csharp
public sealed partial class Note : Aggregate<Guid, Versioned>
{
    public string Title { get; private set; } = "";

    public void Rename(string title)
    {
        Refuse.When(string.IsNullOrWhiteSpace(title), () => Title, "A note needs a title.");
        Title = title.Trim();
    }
}

Form.For(Note.Rename, id)[f => [
    Ui.Field.Bind(() => f.Title).Label("New title"),
    Ui.Button.Primary.Submit["Rename"],
]]
```

- **The fields are the parameters**, each named as the parameter, capitalised — `title` is `f.Title` — and filled from
  the aggregate's property of that name and type when it has one — or of that name holding the same value one side
  of a value object of one value: a `decimal? totalSum` is filled from `Amount? TotalSum` (`TotalSum?.Value`, empty for
  none), an `Amount? paid` from `decimal? Paid`. A value object of one value (`DriverName(string
  Value) : Text(…)`) is its value on the form, with its rules told to the control (`maxlength`, `min`, …) and made
  through `Create` on submit, every refusal under its field at once; a nullable one through `CreateOptional`. A text or a
  bool is taken as it is; a number, a day, an enum or an id is held empty until one is typed — never a `0` nobody
  chose — and refused as required when left so. An operation's form still starts from what the aggregate holds; only
  a factory's, or a parameter the aggregate has nothing alike for, starts from the parameter's declared default
  (`int count = 3` shows 3) — or empty, when it declares none or a null one.
- **A parameter's attributes tell its field**, as a model property's do: `[DataType(DataType.Date)] WallMoment? deliveredAt`
  (a `Moment` value object, or a plain `DateTime?`) is drawn by `Ui.Field.Bind` as a `Ui.DatePicker` alone, not a
  day and a time.
- **The domain's rules are the form's.** As each field is committed — a field typed into commits as it is left
  here, unless it says `.Live()`, `.Debounce(…)` or `.Defer()` ([bind timing](#bind-timing)) — the operation is tried
  (`Note.DryRunRename(…)`, [nothing written](domain.md#an-operation-run-as-one-write)) and what it would be refused for —
  a `Refuse`, an attribute, `Validate()` — is shown under its field before the save is tried. The submit calls the
  entry `Note.Rename(id, version, title)` at the version the form was read at, so a save somebody made meanwhile is
  refused at the top of the form.
- **Keep, clear or set: `Change<T>`.** A parameter that may leave its value alone is a `Change<T>` (`Rask.Values`):
  `public void Change(Change<Secret> password, Change<Nickname?> nick) => (Password, Nick) = (password.Applied(Password)!, nick.Applied(Nick));`.
  Its field starts **empty**, and an empty field always **keeps** the value — the password case: nothing typed, the
  stored password stays, and it is never read into the form. A value typed in is `Change<T>.Set(…)`, made through the
  value object's `Create`. Over a value that may be absent (`Change<Nickname?>`), `Ui.Field.Bind(() => f.Nick)` also
  draws a "Clear" ("Törlés") beside the control; ticked, the operation is handed `Change<T>.Clear`. From code,
  `Change<T>.Keep` is the default. A password value object (`Password` base) is never read into any form field.
- **Who may.** A principal the operation's roles do not admit (`Note.MayRename` is false) sees the form with every
  control disabled, the submit among them.
- **A factory** is a form too, empty to start: `Form.For(Note.Write)[…]` saves through `await Note.Write(…)`.
- `.Missing(component)` is drawn for an id no row has, `.OnSaved(async () => …)` runs after a save — closing a dialog,
  say. A form handed another id reads that aggregate afresh.
- **Every step a hand form takes.** The `<form>` it draws takes what `Form.Model(m)` does — `.Id(…)`, `.Class(…)`,
  `.Style(…)`, `.Data(…)`, `.Attributes(…)`, `.ConfirmLeave("Leave without saving?")` and
  `.OnInvalidSubmit(m => …)` — so moving a hand form to `Form.For` loses none of them. `.Saved("Company saved.")` is
  what is toasted once it saves: by `Ui.EditorDialog` in place of its own "Saved" (the catalog's `EditorSaved`), or by
  the form itself when nothing hosts it.
- **Rows: a list parameter.** An operation that takes a list of rows — the `Children<TEntity, TSpec>.Sync` of
  [children made from a form's rows](domain.md#children) — is an editable list: `f.Parts`, a row per child the aggregate holds
  (read off the child by the spec's member names, its `Id` among them, so `Sync` matches it), each row's fields the
  spec's members. Write the rows with the kit's table, a row added with `f.Parts.Add(new())` and taken away with
  `f.Parts.Remove(row)`:

  ```csharp
  public sealed record SupplyPartSpec(SupplyPartId? Id, TankId Tank, Quantity Quantity);

  public void Change(IReadOnlyList<SupplyPartSpec> parts) =>
      _ = Parts.Sync(parts, s => s.Id, SupplyPart.For, (p, s) => p.Hold(s));

  Form.For(Supply.Change, id)[f => [
      Ui.Table[
          Ui.TableColumns[Ui.TableColumn["Quantity"], Ui.TableColumn.End[""]],
          Ui.TableRows[f.Parts.Select((row, i) => Ui.TableRow.Key(i)[
              Ui.TableCell[Ui.Input.Bind(() => row.Quantity), Ui.Error.For(() => row.Quantity)],
              Ui.TableCell.End[Ui.Button.Ghost.Sm.OnClick(() => f.Parts.Remove(row))["Remove"]]
          ])]
      ],
      Ui.Button.OnClick(() => f.Parts.Add(new()))["Add a part"],
      Ui.Button.Primary.Submit["Save"],
  ]]
  ```

  A member is held as a field is: a one-value value object as its value, made through its `Create`; a number, a day,
  an enum or an id held empty until one is typed, and refused as required when left so; a `bool` as it is. Each row
  is made into its spec, and every refusal is shown under that row — what a value object says, what the operation's
  `Refuse` says (`() => parts[i].Quantity`), what the child's own rules say as the dry run saves it — as
  `Parts[1].Quantity`, the row's place. The child's attributes and the rules stated for it (`[MaxLength]`,
  FluentValidation's `RuleFor`) tell the row's controls (`maxlength`, `min`). A spec is built through a public
  constructor naming every member (a positional record), else a public parameterless one and setters.
- **A value object of several values** — `Range`, `record Opening(DateOnly? Date, decimal? Quantity) : Value` — is a
  group of fields, one per member: `Ui.Field.Bind(() => f.Opening.Date)`. It is made through its `Create`, so its
  `Rules()` refuse under `Opening.Quantity` as the field is left, a member it may not lack is required under its own
  name, and a nullable one (`Opening? opening`) left wholly empty is handed over as null, nothing in it required until
  one is filled. A range is one field, as on a generated model: `Ui.Field.Bind(() => f.Km)` draws both ends under one
  label, each told the range's kind and bounds.
- **A field is named by its whole path.** A control bound to a member of a value object or a row posts under the
  path its refusals are told by — `Ui.Input.Bind(() => f.Opening.Quantity)` is `name="Opening.Quantity"`, a row's
  `name="Parts[0].Start.Quantity"` — never the last member alone, so it does not clash with a `Quantity` beside it. Its
  id is drawn from the path as written, indexes left out (`f-opening-quantity`), so a row's field keeps its id as the
  rows before it are removed; two that derive one id are numbered (`f-start-quantity-2`).
- **Any number of parameters.** An entry past the sixteen a `Func` holds — an operation of fifteen parameters, with its
  id, version and token — is taken as a delegate the form declares for it (`SupplyOperationForms.ChangeEntry`), which
  `Supply.Change` converts to as it does to a `Func`: `Form.For(Supply.Change, id)` reads the same. Grouping what
  belongs together into value objects still reads better.
- **A saved form reads the aggregate afresh**, so its next submit is at the version the save left and a row it added
  is matched by the id it was given, not added twice. A factory's form starts empty again.
- **The model, read as the form reads it: `await SupplyChangeInput.Of(id)`.** The fields filled from the stored
  aggregate at its version, through the same read `Form.For(Supply.Change, id)` makes — what a test hands
  [`Test.FormShows(…)`](testing.md#the-form-says-what-the-domain-says-testformshows). It throws when nothing is stored
  under the id; a factory's input has none, as its form starts empty.
- **Two operations of one signature each get their form.** `Form.For` picks the form by the delegate the method
  group converts to, and `Product.Archive` and `Product.Restore` — each taking only the id and version — would convert
  to the same one. Each is then given a hidden overload that takes a mark of its own first
  (`ProductOperationForms.ArchiveMark`), and its form takes a delegate of that shape, so `Form.For(Product.Archive, id)`
  and `Form.For(Product.Restore, id)` still pick one each.
- An operation whose parameter no field holds — a row whose member is itself a list or a value object of several
  values — gets no `Form.For` ([RASK188](diagnostics.md#rask188)).
