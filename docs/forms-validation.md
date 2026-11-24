# Forms — validation

Write the rule where the field is: `.Validate(…)` on an input, or on the form for a rule that spans
fields. DataAnnotations and FluentValidation are [also supported](#also-supported-dataannotations).

‹ Back to [Forms & validation](forms.md)

## Inline validation

A rule takes the value and returns the messages that reject it. An empty sequence means valid.

```csharp
Input.Bind(() => _model.Email)
    .Validate(v => v.Contains('@') ? [] : ["Email looks wrong."])
```

There is no package to add and nothing to register. The rule runs when the field's value arrives —
with the next action, unless the field says `.Live()` or `.Blur()` — and again on submit
([When the rules run](#when-the-rules-run)).

**A second `.Validate(…)` adds a rule.** They run in the order written, and a rule is not asked about a
value an earlier one rejected:

```csharp
Input.Bind(() => _model.Name)
    .Validate(DestinationName.Check)      // required, length, format
    .Validate(NameIsFree)                 // then, only if the first let the value through
```

### A rule across fields

A rule that no single field owns goes on the form. It runs on submit, and its messages belong to the
form rather than to an input, so they show in `Validation.Summary`:

```csharp
Form.Model(_model)
    .OnSubmit(m => _submission = "Welcome")
    .Validate(m => m.Password == m.Confirm ? [] : ["Passwords do not match."])[
    Input.Bind(() => _model.Email)
        .Validate(v => v.Contains('@') ? [] : ["Email looks wrong."]),
    Validation.Message.Template(errs => Div.Class("err")[errs[0]]).For(() => _model.Email),
    Validation.Summary.Template(SummaryAlert),
    Button.Type(ButtonType.Submit)["Sign in"]
]
```

<!-- demo:validation-inline -->

### Rules in a value object

A field's simple rules — required, length, format — belong to the value, not to one form. Declare the
value object from one of Rask's [base records](data.md#declared-from-a-base-record) (`Rask.Values`, in the `Rask` package, so
a browser app has them too) and its constructor
checks them — `public sealed partial record DestinationName(string Value) : Text(Value, MinLength: 2, MaxLength: 255);`
— and a form bound to the aggregate's generated model asks the same rules
[by itself](#how-a-control-knows-a-field-is-required), with nothing written on the field.

A form over a model of your own, with no data layer behind it, names the value object's rules from the field
instead. Every value object of one value — declared from `Text`, `Number`, `Whole`, `Day` or `Moment`, or `: Value`
with one member — gets a generated `X.Check(value)`, which answers what `X.Create` would refuse with, in the shape of
a field's rule:

```csharp
using Rask.Values;

public sealed partial record DestinationName(string Value)
    : Text(Value, MaxLength: MaxLength, Pattern: @"^\p{L}", Message: "A name starts with a letter.")
{
    public const int MaxLength = 255;
}

Ui.Input.Bind(() => _model.Name)
    .MaxLength(DestinationName.MaxLength)
    .Validate(DestinationName.Check)
```

`Validate` takes it by name: the value in, the messages out, synchronously. Every form that edits a destination's name
asks the same question the domain asks, and the constant sets the input's `maxlength` too. A number gets `Check` for
the value and for its nullable form, so it fits a `decimal` field and a `decimal?` one. Any static method of that shape
works the same way; one named `Validate` on a value object an aggregate holds is never run by itself
([RASK132](diagnostics.md#rask132)) — declare that one from a base record.

<!-- demo:validation-value-object -->

**The framework's own sentences, for the common rules.** `Rules` answers in the reader's language from the
[strings catalog](localization.md#translating-the-frameworks-own-text) — the kit carries the Hungarian — so a
rule that only needs "filled, and not too long" writes no words of its own:

```csharp
Ui.Input.Bind(() => _model.Driver).Validate(name => Rules.Text(name, 100))
Ui.Input.Bind(() => _model.Note).Validate(note => Rules.Optional(note, 500))
Ui.Input.Bind(() => _model.Litres).Validate(Rules.Given)   // a decimal? that must be given
```

| Rule | Says | Hungarian |
|---|---|---|
| `Rules.Text(value, max)` | `Required` when empty or blank, `TooLong(max)` past `max` | |
| `Rules.Optional(value, max)` | `TooLong(max)` past `max`; empty is fine | |
| `Rules.Given(value)` | `Required` when a nullable value is null | |
| `Rules.Required` | *This field is required.* | *Kötelező kitölteni.* |
| `Rules.TooLong(max)` | *Enter at most 100 characters.* | *Maximum 100 karakter hosszúnak kell lennie.* |

A `[Required]` or a `[StringLength(100)]` on a model says the same sentences ([below](#in-the-readers-language)).

A model whose data layer has registered its properties' rules needs no step at all: the field runs the
rule its property's type carries, first, and the steps written on the field run after it, only for a
value it accepted. It runs when a written step would — whenever the field's value is written to the
model, and on submit.

```csharp
Input.Bind(() => _model.Name)             // DestinationName's rules run, on this field's own timing
    .Validate(NotOnTheBlocklist)          // then this, for a name the first rule accepted
```

This belongs to the form and the bound field, so a plain `Input`, `Select` or `Textarea` has it exactly
as `Ui.Input` does.

**A [Rask.Data](data.md#what-a-form-over-the-model-asks-by-itself) aggregate's model is such a model.**
A property the aggregate declares non-nullable is required by its type, with no `[Required]` and with the
automatic validators on or off; a plain property that cannot be null (`int Weight`, `string Plate`) is required
the same way, in the framework's one sentence — *This field is required.*, in the reader's language.

A value object declared from one of Rask's [base records](data.md#declared-from-a-base-record) —
`public sealed partial record DriverName(string Value) : Text(Value, MaxLength: 1000)` — is asked by every field
bound to it on the aggregate's generated model (`DeliveryModel.Driver`), with nothing written on the field: its
rules are the base's arguments and its own `Rules()`, checked by its constructor, and the field runs its generated `DriverName.__Problems`,
which makes the value through that constructor and answers without an exception. The messages (*Enter at most 1000
characters.*, *Enter 1 or more.*, *Choose a date on or before …*) come from the strings catalog in the reader's
language. A nullable one left blank says nothing. A `Range` or `Value` record is a nested model, and a refusal its
`Create(…, "Km")` throws on submit is shown under the member it names (`Km.End`). A range the aggregate always holds
is on the model from the start (`= new()`), so `() => m.Km!.End` binds with no null in the path, and each end runs the
range's own rule as it is committed — the end against the start beside it, strict or not, and both within `Min`/`Max`
— by asking the generated `KmRange.TryCreate`, so the form says *It must be after the start, 10.* under `Km.End`
before the save does.

**Field shapes.** The generated model also tells every control bound to it what its field is — a
`Rask.Core.Forms.FieldShape`, registered per model type through `RaskValidation.RegisterFieldShapes`: the kind
(`FieldKind.Text`, `Number`, `Whole`, `Day`, `Moment`, `Range`, `Id`, `Choice`) and the rules the value object's base call
states as constants (`MaxLength`, `MinLength`, `Min`, `Max`, `Scale`, `NotAfterToday`, `NotAfterNow`; a range's `Strict`, the
names of its two ends and their kind). Each value object answers its own generated `X.__Field`; a member holding
another aggregate's typed id is `FieldKind.Id`; an enum is `FieldKind.Choice` with its members and their labels
(`[Display(Name = …)]`, else the member's name), written out at compile time so nothing reflects over the enum. A
plain `string`, number, date or `bool` says what it is through its type, and is registered only when its
DataAnnotations attributes say more (below). A range's ends whose
type is not a whole number, a number or a `DateOnly` — a `DateTime` moment — have `Ends = null`. A bound text input
or textarea is given the declared lengths as its `maxlength` and `minlength`, so the browser stops at 1000
characters instead of the save refusing the 1001st; a `.MaxLength(…)`/`.MinLength(…)` written on the control wins.
A value object declared `Text(Value, Upper: true)` has `Upper`, and every input or textarea bound to it draws what is typed in capitals
(`uppercase`, the text-transform only: the value object puts the value in capitals as it is made).

**What a member's attributes tell its control.** Beside the rules, a `FieldShape` carries the hints a property's
attributes give, read at compile time for a generated model and off the property itself for a model written by
hand (a model type that registered shapes answers for itself; a property it answers `null` for is read off its
attributes):

| Attribute | `FieldShape` | A bound control |
| --- | --- | --- |
| `[StringLength]`, `[MaxLength]`, `[MinLength]`, `[Length]` | `MaxLength`, `MinLength` — the tightest they say, and on a value object's property the tighter of the attribute and the value object's facet | `maxlength`, `minlength` |
| `[EmailAddress]`, `[Phone]`, `[Url]`, `[DataType(Password \| EmailAddress \| PhoneNumber \| Url \| ImageUrl \| Date \| Time \| DateTime)]` | `Input` | the input's `type`, where it fits what the member holds: any of them on a `string`, `Date` on a `DateTime` too (shown and read back as its day) |
| `[Editable(false)]` | `ReadOnly` | `readonly` on an input or a textarea, which then wires no write; `Ui.Field.Bind` disables a control that cannot be read-only |
| `[DisplayFormat(ConvertEmptyStringToNull = false)]` | `KeepEmpty` | an emptied `string?` is written `""`, not `null` |
| `[UIHint("…")]`, `[DataType(DataType.MultilineText)]` | `Hint` | `Ui.Field.Bind` draws `Ui.Switch` for `FieldHints.Switch` on a `bool`, `Ui.RadioGroup` for `FieldHints.RadioGroup` on an enum, `Ui.Textarea` for `FieldHints.MultilineText` |

A step written on the control wins over each of them — `.Type(…)`, `.MaxLength(…)`, `.MinLength(…)`,
`Ui.Field.Bind(…).Multiline(false)` — except `[Editable(false)]`, which a control cannot be talked out of.

A model written by hand names its rule from the field, as above. The registration is
`RaskValidation.RegisterFieldRules(typeof(DestinationModel), property => …)`, made once per model type
by the layer that generates the model; it is keyed by the type that declares the property, so a nested
object and a row of a collection bring their own.

### How a control knows a field is required

A bound control announces a field that must be filled as `aria-required="true"`, so a screen reader says
"required" on the way in rather than only once Save has refused. It asks one question, with the expression
it is bound with — `RaskValidation.IsRequired(() => m.Name)` — which answers yes when either is true:

- the member carries `[Required]`;
- the type that declares the property registered it:
  `RaskValidation.RegisterRequiredFields(typeof(DestinationModel), property => property is "Name")`, made once
  per model type by the layer that generates the model. This is how a field that is required **by its type**,
  with no attribute, still announces itself. A registration says a field is required and enforces nothing, so
  register only what a rule refuses when it is left empty.

A plain `Input`, `Select` and `Textarea` write it, and so does every `Ui.*` control, on the element that
carries its other `aria-*`. It is only `aria-required`, never the native `required` attribute, which would
switch the browser's own validation bubble on. An `aria-required` written on the control wins:
`Input.Bind(() => m.Name).AriaRequired(false)`. A radio, a range, a slider and a plain `group` write none,
because ARIA has no required state for their roles. A labelled kit control also shows it: Flux's badge beside the
label says *Required* (`RaskString.FieldRequired`), hidden from assistive tech, which hears `aria-required`.

A registration is keyed by the type that declares the property and covers the types derived from it. A path
is followed through the declared types of the properties it walks, and an index stands for the collection's
item type, so `IsRequired(() => order.Rows[2].Weight)` asks what the row's type registered for
`Weight`, and `order.Price.Amount` asks the type of `Price`. The answer is worked out once per field and kept until
a type is registered again, so asking on every render costs a lookup.

**A part of the model that may be left empty.** One nested type can be held where it must be there and where
it may be absent — `Address Shipping`, `Address? Billing` — so "may be left wholly empty" is said of the
property that holds it: `RaskValidation.RegisterOptionalFields(typeof(OrderModel), property => property is
"Billing")`, made by the same layer. Nothing under such a property is required, however deep:
`RaskValidation.IsRequired(() => order.Billing.Street)` is false while `() => order.Shipping.Street` is true:
a bound control asks with its own bind expression, so it answers for the place it sits in. The rule a field's type carries is asked there only about a value that was
filled in (not `null`, blank text or an unticked box). It relaxes what a form asks and enforces nothing: the
save still says what a part that *was* given lacks.

A field required only by a `Validate` step, a FluentValidation rule or a custom attribute is not visible to
this question; say it on the control with `.AriaRequired()`.

### An async rule

A rule that has to ask something is the same step with an `async` lambda or method. There is no second
name to learn:

```csharp
Ui.Input.Bind(() => _model.Code).Validate(async code =>
    await codes.IsReserved(code, Current.Cancellation) ? [$"\"{code}\" is reserved."] : [])
```

**The latest value wins.** A new change cancels the check still in flight through
`Current.Cancellation`, which anything awaited inside the rule picks up, so an answer for an older
value never lands on a newer one.

A field's async rule runs each time its value arrives: with the next action for a plain field, at each
pause in typing for a `.Live()` one, on leaving a `.Blur()` one (see
[When the rules run](#when-the-rules-run)). A check too expensive even for that belongs in the form's
rule, which runs once, on submit:

```csharp
Form.Model(_model).OnSubmit(Redeem).Validate(async m =>
    await codes.IsReserved(m.Code, Current.Cancellation) ? ["That code is reserved."] : [])[ … ]
```

<!-- demo:validation-inline-async -->

A form's check is a convenience for the reader, never the control: check again where the data is
written.

### The database said no

Some rules only the store can check: a name that must be unique, a booking that must not overlap
another. When the submit handler throws an exception that names the fields it is about, the form shows
each message under its field. The reader stays on the page, and the handler has no `try`/`catch`:

```csharp
Form.Model(_model).OnSubmit(Save)[
    Ui.Input.Bind(() => _model.Name).Label("Route"),
    Ui.Button.Submit["Save"]
]

Task Save(RouteModel route) => Route.Create(route);   // "A route with this name already exists." under Name
```

The message is drawn exactly as a rule's message is, and it goes away when the reader changes the field.
A failure over several fields — a unique pair of year and number — shows under each of them, and changing
any one clears it from all. The next submit validates again from the start.

Any exception can do this by implementing `IFieldFailures`. A failure is a message and the fields it
is shown under, named as the form's model names them (`Name`, `Price.Amount`, `Lines[2].ValidFrom`):

```csharp
public sealed class RouteNameTakenException() : Exception("The route name is taken."), IFieldFailures
{
    public IReadOnlyList<FieldFailure> Failures { get; } =
        [new("A route with this name already exists.", [nameof(RouteModel.Name)])];
}
```

`Marked` names fields that turn invalid without a message of their own. An overlapping booking says so
under the driver and marks the two dates:

```csharp
new FieldFailure("This driver is already booked then.", ["DriverId"], Marked: ["ValidFrom", "ValidTo"])
```

A failure that names no field on this form shows where the form's own messages do, in
`Validation.Summary`. A form with no summary says it in an error toast: the failure's own sentence, as it
was written, in Development and in Production alike — never *The save failed*. Several such failures of one
save are one toast, each different sentence once, in order. A save refused for a field and for the whole
record shows both, each in its place.

```csharp
throw new RaskValidationException([new FieldFailure("The month is closed.", [])]);   // the summary, or a toast
```

Only a refusal with nowhere at all to be read — no bound field, no summary, and a hand-wired host whose
layout draws no toasts — fails as any other submit does: see [a save that fails](#a-save-that-fails).

The same exception thrown by a handler that is not a form's submit — the confirm button of a list's bin —
is a toast too: see [a handler that refuses](composition-lists.md#a-handler-that-refuses).

It is not logged as an error: the reader was told, and nothing is broken.

<!-- demo:validation-refused-save -->

A `System.ComponentModel.DataAnnotations.ValidationException` thrown by the save is a refusal too, with
no interface to implement: its message goes under the fields its `ValidationResult.MemberNames` name,
into `Validation.Summary` when it names none on the form, and into a toast when the form has no summary.

```csharp
throw new ValidationException(new ValidationResult("That name is taken.", [nameof(RouteModel.Name)]), null, null);
throw new ValidationException("The month is closed.");   // no field: the summary, or a toast
```

That is the type itself. A class of your own derived from it is an exception like any other.

#### …and it is checked before you save

A model whose store has registered its rules is asked while the form is filled in, with no step on the
field and none on the form:

```csharp
Form.Model(_destination).OnSubmit(Save)[
    Ui.Input.Bind(() => _destination.Name),   // no unique step
    Ui.Button.Submit["Save"]
]
```

The store is asked when a field is **committed** — its value written to the model at a moment the reader
stopped: the next action for a plain field, the pause of `.Live()` or `.Debounce(…)`, leaving a `.Blur()`
field — and once more on submit, for
the whole model. It is never asked about a value still being typed into a field that writes every
keystroke, and never about a value one of the field's own rules rejects; on submit it is asked only once
every rule on the form has passed, so a known duplicate does not reach the save.
A field under a unique rule keeps the default binding, as above: the name it holds is asked with the
reader's next action, and a taken one is told at Save.

What it says is shown exactly as a refused save is: under each field the failure names, the `Marked`
ones only turned invalid, gone from all of them at the first keystroke of a correction. A value the store
has already answered for is not asked about again until some field changes.

The save can still be refused — someone else took the name in between — and that is shown the same way.
If the store cannot be asked, the form says nothing and the save decides; the failed check is logged as a
warning.

**Rask.Data registers them for an aggregate's generated model**: every unique index its `Configure`
declares with a message, `IsUnique("…")`, is asked this way by a form bound to `DestinationModel` — for
the current tenant's rows only, and leaving out the row an edit form was filled from. A model written by
hand is not asked about; its save still is. See
[what a form over the model asks by itself](data.md#what-a-form-over-the-model-asks-by-itself).

A store announces its rules for a model with `RaskValidation.RegisterStoreRules(typeof(DestinationModel),
services => rules)`, where `rules` is an `IStoreRules`: one method, given the model and the path of the
committed field (`null` on submit), handing back `FieldFailure`s. A model no store registered is not
asked about, and its form awaits nothing extra.

None of this needs the kit. The form and its bound fields do it, a plain `Input`, `Select` or `Textarea`
with `Validation.Message` beside it shows it, and a bound control says `aria-invalid="true"` by itself
while its field holds a message or is marked by one told under another field.

### A save that fails

A save can also throw something that is nobody's fault to correct: the database is down, a service timed
out. The form tells the reader, with no code for it:

```csharp
Form.Model(_model).OnSubmit(Save)[
    Ui.Input.Bind(() => _model.Name).Label("Route"),
    Ui.Button.Submit["Save"]
]

Task Save(RouteModel route) => Route.Create(route);   // throws TimeoutException
```

The reader sees an error toast, *The save failed. Please try again.* — `RaskString.FormSaveFailed`, with
its Hungarian in the kit — what they typed is still in the fields, and the button works again. A form
that draws its own messages with `Validation.Summary` says the sentence there instead, and no toast is
raised. The exception is logged as an error, and it is the `f.Error` of `Form.Model(m)[f => [ … ]]`.

The sentence never carries the exception's text: that is written for the log and can name a server or a
user. A server app running in Development adds the exception's type and message to it, so you see what
broke without opening the log.

A cancelled save (`OperationCanceledException`) says nothing, and a `RaskApp` and the browser host
always have somewhere to show a toast. A hand-wired host whose layout has no `Ui.Toast` or `ToastOutlet`
shows none; the log entry is still written.

To say something else, catch it where you save. What the handler catches never reaches the form:

```csharp
async Task Save(RouteModel route)
{
    try { await Route.Create(route); }
    catch (TimeoutException) { Toast.Warning("The server is busy. Your route is not saved yet."); }
}
```

<!-- demo:validation-save-fails -->

### What validation walks, and what it leaves alone

Every check walks the model's object graph — its properties, the items of its lists, the values of its
dictionaries — so a rule on an address inside an order is heard with nothing declared. The walk never goes
into a query (`IQueryable`, a `DbSet`), a delegate, a stream, a task or reflection's own objects, and stops at
32 levels or 10,000 objects. Two more things are left alone:

- **What you mark `[SkipValidation]`.** On a property, nothing under it is read or validated; on a class or a
  struct, no object of that type is walked into (the form's own model is always validated). ASP.NET Core's
  `[SkipValidation]` (`Microsoft.Extensions.Validation`) is honoured the same way.

  ```csharp
  public sealed class OrderForm
  {
      public List<OrderLine> Lines { get; set; } = [];

      [SkipValidation]                 // the customer's whole history: not the form's to check
      public Customer? Customer { get; set; }
  }
  ```

- **An Entity Framework navigation that is not loaded.** Reading it would load it — a query per navigation per
  check. An entity that lazy-loads holds EF's `ILazyLoader` (a proxy, or the "injected loader" pattern), and the
  walk asks it `IsLoaded` before it reads a navigation; one not loaded is skipped, never loaded. Rask finds the
  loader by its type's name, so `Rask` takes no dependency on EF. The delegate pattern
  (`Action<object, string>`) cannot be asked: mark such a navigation `[SkipValidation]`. A DataAnnotations
  attribute on a navigation itself (`[Required] Customer`) is still read by .NET's validator when its object is
  checked.

### When the rules run

A field's rules run when its value reaches the model, and a bound field says nothing until the next
action. So a plain form shows no message while the reader types or moves between fields: the messages come
when they press Save, and each one goes at the first key of its correction.

`.Live()`, `.Debounce(…)` and `.Blur()` move a field's rules to the moment it speaks:

```csharp
Ui.Input.Bind(() => _model.Traveller).Label("Traveller")
    .Validate(name => name.Length > 0 ? [] : ["Who is travelling?"]),   // runs with the next action: Save
Ui.Input.Bind(() => _model.Name).Label("Destination")
    .Live()                               // runs as the reader types, 150 ms after the last key
    .Validate(DestinationName.Check)
    .Validate(NameIsFree),                // a lookup: only for a name the first rule accepted
Ui.Textarea.Bind(() => _model.Notes).Label("Notes")
    .Blur()                               // runs on leaving the field
    .Validate(notes => notes.Length <= 40 ? [] : ["Keep the notes under 40 characters."])
```

Both rules of the live field answer in the one round trip its pause makes. Enter and Save send what was
typed before they submit, and a lookup still running when Save is pressed is run again and waited for.
The steps themselves are described under [Bind timing](forms.md#bind-timing).

Two kinds of rule that Rask adds for you follow the same clock. A **value object's** rule follows its
field's bind timing: with the next action for a plain field, at the pause for a `.Live()` one. A rule
answered by the **store** — a unique name, a range that must not overlap — runs when the field writes and
on submit, which for a plain field is on submit.

<!-- demo:validation-bind-timing -->

---

### What a number field says when it cannot read what was typed

A bound number, date or time field is typed text until it reaches the model, and some text does not fit
the member it is bound to: `1.5` in a field over an `int`, `99999999999` in one, a lone minus, a date with
only its month typed. Rask does not treat that as an empty field, and never as the value the model already
held. The model is left as it was, the field says what it could not read, and the save does not run:

| Bound type | Typed | The field says |
|---|---|---|
| `int`, `long`, `short`, `byte` and their unsigned kin | `1.5`, `-`, `abc`, `1e400` | `Enter a whole number.` |
| `decimal`, `double`, `float` | `-`, `12.5.0`, `1e400` | `Enter a number.` |
| any number | `99999999999` in an `int`, `-5` in a `uint`, `1e30` in an `int` or a `decimal` | `The number is out of range.` |
| `DateOnly`, `DateTime`, `DateTimeOffset` | a date half typed, `2026-02-31` | `Enter a valid date.` |
| `TimeOnly`, `TimeSpan` | a time half typed, `25:99` | `Enter a valid time.` |

`1e400` is more than a number input can hold at all: the browser hands it over as text it could not read,
not as a number too large, so the field says the number sentence. Out of range is a number the browser did
read and the member cannot hold.

```csharp
Form.Model(_tank).OnSubmit(Save)[
    Ui.Input.Bind(() => _tank.Weight).Label("Weight (kg)"),   // int: "1.5" → "Enter a whole number."
    Ui.Button.Submit["Save"]
]
```

It is a message like any other: under the field (`Ui.Error`, `Validation.Message`), with `aria-invalid` on
the control, gone at the first key of the correction. Three things set it apart from a rule you wrote:

- **It comes first.** A required field says this instead of "required" — the reader did type something —
  and an inline `.Validate(…)` is not asked at all, because there is no value to hand it. Once a value
  arrives that fits, the field's own rules run as usual.
- **It needs nothing switched on.** No attribute, no rule, and `AutoValidate(false)` does not remove it.
- **The submit handler does not run** until the field holds a value that was read. On an edit form that is
  what keeps the stored value from being saved in place of the one on screen.

The sentences are framework text (`RaskString.ValidationWholeNumber`, `ValidationNumber`,
`ValidationNumberRange`, `ValidationDate`, `ValidationTime`): the UI kit carries their Hungarian, and an app
overrides or translates them in `Resources/RaskStrings.{culture}.json` ([localization](localization.md)).

**The browser's own bubble does not get there first.** A browser refuses to submit a form over a number
input holding `1.5` when the step is whole, or over a date half typed, and says so in its own words and its
own language; and it reports such a field's value as empty. Rask's runtime tells the page the field held
something it would not hand over, calls the bubble off and lets the submit go, so the page refuses it with
the sentence above. That applies only to what Rask can speak for: a constraint you wrote yourself
(`required`, `pattern`, `min`, `max`, a `step` of your own) is still the browser's to enforce — and so is
the whole form when its handler is `OnAnySubmit`, which takes what was posted whatever the rules say.

**How a number is spelled.** A number input always sends digits and a dot, whatever the reader typed:
`12,5` in a Hungarian browser arrives as `12.5`. Such text is read the same in every culture, and `1e3`
is read as 1000. A number that arrives spelled another way — written by a test, or by hand into a frame —
is read in the session's culture: `12,5` and `1 234,5` under `hu`, `1,234.5` under `en`, with separators
accepted only around groups of three. Anything else is not a number, and the field says so; a comma is
never quietly read as a separator of thousands.

**A number in a text field speaks the reader's language both ways.** A numeric member bound to a field
typed into as text (`.Type(InputType.Text)`, often with `.InputMode(InputMode.Decimal)`) is *shown* with
the reader's decimal mark — `1234,5` under `de` and `hu`, `1234.5` under `en` — and *read* in the same
culture, every time: under `de`, `1.234` is one thousand two hundred and thirty-four and `1,234` is a
fraction. What the field shows reads back as the same number. A number input (`type="number"`) is not
this: the browser sends it in digits and a dot, and it is read so. An amount (`Ui.Input.Money()`) keeps
its own marks.

**The kit's typed pickers follow the same rule.** `Ui.DatePicker` and `Ui.TimePicker` with
`Type(…Input)` are typed a part at a time. Left with parts that make no value — a day and no month, an
hour and no minute, one end of a range — the field says `Enter a valid date.` or `Enter a valid time.`
and the save is refused; the value the picker held before is not saved in its place. With every part
emptied the picker is simply empty: `null` where the member is nullable, and a required one says so.

The demo under [When the rules run](#when-the-rules-run) has such a field: type `1.5` into Travellers and
press Save.

---

## Also supported: DataAnnotations

A model that already carries `[Required]` and its relatives is validated by them. There is no package
to add and nothing to declare in the form: `HTMLFormElement<TModel>` registers the pass itself, and one
registration covers the whole reachable model graph. Inline rules run first, and both can guard one form.

```csharp
public sealed class SignupModel
{
    [Required, StringLength(20, MinimumLength = 3)] public string Username { get; set; } = "";
    [Required, EmailAddress]                        public string Email    { get; set; } = "";
}

Form.Model(_model).OnSubmit(m => Console.WriteLine(m.Username))[
    Input.Bind(() => _model.Username),
    Validation.Message.Template(errs => Div.Class("err")[errs[0]]).For(() => _model.Username),
    Input.Bind(() => _model.Email),
    Validation.Message.Template(errs => Div.Class("err")[errs[0]]).For(() => _model.Email),
    Button.Type(ButtonType.Submit)["Register"]
]
```

<!-- demo:validation-fields -->

Supports `[Required]`, `[EmailAddress]`, `[Range]`, `[StringLength]`, `[RegularExpression]`, custom
`ValidationAttribute` subclasses, and `IValidatableObject`. Unlike the BCL's
`Validator.TryValidateObject`, Rask invokes `IValidatableObject.Validate` even when attribute errors
exist — so attribute and object-level errors surface together (ASP.NET Core MVC parity). The
`ValidationContext` is built with the render-scoped `IServiceProvider`, so custom attributes can call
`ctx.GetService<T>()`.

A `ValidationResult` with empty `MemberNames` lands on the form-level slot (`Validation.Summary`); a
populated one tags the named field.

### The name a message gives the field

A message's `{0}` — "The **Work email** field is required." — is the field's display name: its
`[Display(Name = …)]` (through its `ResourceType` when it names one), else the member's own name. That is the ONE
way Rask names a field — on the form, on a dispatched request, in an async pass, in a FluentValidation message and in
a Rask.Data model's generated server checks (the generator copies `[Display]` onto `<Entity>Model`).
`System.ComponentModel.DisplayNameAttribute` is read nowhere; [RASK131](diagnostics.md#rask131) warns on one and its
quick-fix rewrites it as `[Display(Name = …)]`. A field around a bound kit control — `Ui.Field[…]` with no `Ui.Label`, or `Ui.Field.Bind` — is labelled by the same name; a bare control is not — see [Labels](forms.md#controls-at-a-glance).

```csharp
public sealed class SignUp
{
    [Required, Display(Name = "Work email")] public string? Email { get; set; }   // "The Work email field is required."
    [Required, Display(Name = "Given name")] public string? First { get; set; }  // "The Given name field is required."
}
```

### An async attribute

.NET 11's asynchronous rules work in a form with nothing declared: an `AsyncValidationAttribute` is awaited
(`IsValidAsync`), and an `IAsyncValidatableObject`'s `ValidateAsync` runs in place of its `Validate`. A uniqueness
check is an attribute on the model, its lookup a service from the scope:

```csharp
public sealed class SignUp
{
    [Required, EmailAddress, UniqueEmail] public string Email { get; set; } = "";
}

public sealed class UniqueEmailAttribute() : AsyncValidationAttribute("{0} is already registered.")
{
    protected override ValidationResult? IsValid(object? value, ValidationContext context) =>
        throw new NotSupportedException("Looked up asynchronously.");

    protected override async Task<ValidationResult?> IsValidAsync(object? value, ValidationContext context, CancellationToken ct)
    {
        var users = context.GetRequiredService<IDbContextFactory<AppDb>>();
        await using var db = await users.CreateDbContextAsync(ct);
        return await db.Users.AnyAsync(u => u.Email == (string)value!, ct)
            ? new ValidationResult(FormatErrorMessage((string)value!), [context.MemberName!])
            : ValidationResult.Success;
    }
}
```

<!-- demo:validation-async-attribute -->

- **Synchronous rules first.** `[Required]` and `[EmailAddress]` run as before; the lookup is asked only once
  they pass, so an empty or malformed address never reaches the database.
- **While it runs** the field reads `IsValidating` and the `Validation.Indicator` shows — the same as any
  [async validator](#async-validators-and-the-validating-indicator). A field with no async rule stays synchronous.
- **Latest wins.** The field checks when its [bind timing](forms.md#bind-timing) says — on change, or at a
  `.Debounce(…)` pause — and a value changed again cancels the lookup in flight: its token is cancelled, and an
  answer that arrives anyway is dropped.
- **A lookup that throws is never a pass.** The field says *Validation could not be completed.*, the form will not
  submit past it, and the exception goes to the [log](observability.md#structured-logging) with the model and member named.
- **The server asks again.** A [dispatched request](validation.md#requests) and a minimal API body await the
  same attributes before the handler runs; only .NET's synchronous `Validator` calls `IsValid`.

### In the reader's language

An attribute with no `ErrorMessage` of its own speaks the reader's language: .NET ships its attributes' English
only, so Rask words every built-in one — `[Required]`, `[StringLength]`, `[Range]`, `[EmailAddress]`,
`[Compare]`, … — from the [strings catalog](localization.md#a-models-validation-attributes), with the attribute's
numbers filled in. A `[Range]` with an exclusive end says so: `[Range(0, 10, MinimumIsExclusive = true)]` is
*0 feletti, legfeljebb 10 értéket adjon meg.* (`ValidationAboveAndAtMost`; `ValidationAtLeastAndBelow` and
`ValidationAboveAndBelow` for the other two). A `[Compare("X")]` naming no property of its model refuses every value, and is
[RASK144](diagnostics.md#rask144): name it with `nameof(…)`. Under `AddRask(culture: "hu")` a bare `[StringLength(100)]` says *Maximum 100 karakter
hosszúnak kell lennie.*; an English reader still reads .NET's own text, word for word. An `ErrorMessage`, an
`ErrorMessageResourceType` and an attribute class of your own always say what they say. It holds on the Server
and in the browser, in a form and in a request validated before its handler runs — so a Hungarian app has no
reason to turn the automatic validators off.

#### Messages from your own resources

An app that keeps its own wording in resources says so the .NET 11 way — the platform's two calls, nothing of
Rask's:

```csharp
builder.Services.AddLocalization();
builder.Services.AddValidation(o => o.LocalizerProvider = (_, factory) => factory.Create(typeof(ValidationMessages)));
```

```xml
<!-- Resources/ValidationMessages.hu.resx -->
<data name="RequiredAttribute_Error"><value>A(z) {0} megadása kötelező.</value></data>
<data name="SignUp_Email_UniqueEmailAttribute_Error"><value>Ezzel az e-mail-címmel már regisztráltak.</value></data>
```

A failed attribute is then looked up with .NET's own keys, in this order — the attribute's `ErrorMessage` itself,
`{Type}_{Member}_{Attribute}_Error`, `{Type}_{Attribute}_Error`, `{Attribute}_Error` — and the template found is
filled in by the attribute (`ValidationAttribute.FormatMessage`): `{0}` is the field's display name (its `[Display]`
name, itself looked up in the same resources), and a `[Range]`'s `{1}` and `{2}` are its bounds. The
same resources word a form, a dispatched request and a minimal API, on the Server and in the browser, in the
reader's language. Without a `LocalizerProvider`, .NET's default applies: resources named after each model. In
the browser a WASM app adds the `Microsoft.Extensions.Localization` package for `AddLocalization()`; Rask fetches
the reader's language's resources before the first render, and the app's other languages right after it.

Your resources are the app speaking, so they come first; the [strings catalog](localization.md#a-models-validation-attributes)
words whatever they have no key for, and .NET's English the rest. An `ErrorMessageResourceType` keeps its own
resource. As in .NET, an `ErrorMessage` of your own is only the FIRST key: where your resources have no entry
for it, the convention keys still apply to it, so a `RequiredAttribute_Error` rewords every `[Required]`. With no `IStringLocalizerFactory` registered, nothing changes. Calling `AddValidation()` does not
switch on .NET's own minimal-API filter for endpoints Rask validates: those keep the one
[rejection shape](validation.md#a-rejected-request).

A custom `ValidationAttribute` (with DI via `ctx.GetService<T>()`):

<!-- demo:validation-custom-attribute -->

`IValidatableObject` runs alongside the attributes, model-level:

<!-- demo:validation-validatable-object -->

---

## Also supported: FluentValidation

Writing the validator is the registration. A generator finds every `AbstractValidator<T>` in your app
at compile time, and a `HTMLFormElement<T>` asks for the one that validates its model — so there is nothing to
declare in the form and nothing to wire in `Program.cs`. It is wrapped as an `IAsyncFieldValidator`,
so async `MustAsync` rules work exactly like synchronous ones.

There is no assembly scan anywhere in this: registration is emitted as a `[ModuleInitializer]`, which
is what lets a WebAssembly app use FluentValidation and still publish trimmed.

```csharp
public sealed class OrderValidator : AbstractValidator<OrderModel>
{
    public OrderValidator()
    {
        RuleFor(x => x.Product).NotEmpty();
        RuleFor(x => x.Quantity).GreaterThanOrEqualTo(1).WithMessage("Quantity must be at least 1.");
    }
}

Form.Model(_model).OnSubmit(m => _submission = "Ordered")[
    Input.Bind(() => _model.Product),
    Validation.Message.Template(errs => Div.Class("err")[errs[0]]).For(() => _model.Product),
    Input.Bind(() => _model.Quantity),
    Validation.Message.Template(errs => Div.Class("err")[errs[0]]).For(() => _model.Quantity),
    Button.Type(ButtonType.Submit)["Order"]
]
```

<!-- demo:validation-fluent -->

Validating one root-model field — when its value arrives — scopes FluentValidation to that single property
(`MemberNameValidatorSelector`, fast path); submit runs every rule. FluentValidation's own
`Cascade(CascadeMode.Stop)` mirrors Rask's first-error-wins gating.

A FluentValidation message names the field by the same `[Display]` name its label and its
DataAnnotations messages read ("'Given name' must not be empty."): Rask puts it behind FluentValidation's
`ValidatorOptions.Global.DisplayNameResolver`. A rule's `.WithName(…)` still wins, and a resolver the app sets
itself replaces Rask's.

A built-in rule with no `.WithMessage(…)` says what the equivalent attribute says through Rask: `NotEmpty()` and
`NotNull()` what `[Required]` says, `MaximumLength`/`MinimumLength`/`Length` what `[StringLength]`/`[MaxLength]`/
`[MinLength]` say, `EmailAddress()`, `Matches(…)`, `CreditCard()`, `InclusiveBetween`/`ExclusiveBetween` (`[Range]`),
`GreaterThanOrEqualTo`/`LessThanOrEqualTo` (a value object's least and greatest), `NotEqual` and `IsInEnum` — the
`RaskStrings` catalog's sentence, Hungarian included, wherever the catalog speaks the reader's language. A rule it has
no sentence for (`GreaterThan`, `Length(n)` exact, `PrecisionScale`, `Must`), and every language it does not speak,
keeps FluentValidation's own translation. Rask puts this behind `ValidatorOptions.Global.LanguageManager`: a rule's
`.WithMessage(…)` always wins, and a language manager the app sets itself replaces Rask's.

An async `MustAsync` rule rides the same wrapper:

<!-- demo:validation-fluent-async -->

### Rules inside the type

The rules can live in the type they guard, the way its `Configure` does: a `static void Rules(InlineValidator<T> rules)`
of any accessibility on an aggregate, an entity, a form model or a value object. It is found at compile time and
registered like an `AbstractValidator<T>` class — no class to write, no scan, trim-safe.

```csharp
public sealed partial class Supplier : Aggregate<SupplierId, Model>
{
    public string Name { get; private set; } = "";
    public string? Email { get; private set; }

    static void Rules(InlineValidator<Supplier> rules)
    {
        rules.RuleFor(s => s.Name).NotEmpty().MaximumLength(200);
        rules.RuleFor(s => s.Email).EmailAddress().MaximumLength(254);
    }
}
```

<!-- demo:validation-fluent-in-type -->

On a primitive member a rule works **exactly like the DataAnnotations attribute with the same rule**:

- **The form.** A `Form` over `SupplierModel`, the generated form model, asks `Supplier`'s rules of the members that
  carry them, live, `MustAsync` included; a child row's rules run under its place (`Parts[1].Quantity`). A rule under
  `When`/`Unless`, a `RuleForEach`, a comparison with another member, or a `Must((s, v) => …)` that reads the
  aggregate needs the aggregate the form does not have: those run at the save.
- **The save.** Every save asks a new row every rule and a stored row only the rules of the members it changes
  (`MustAsync` included), and refuses with one `RaskValidationException` — `Create(model)`, `Update(id, model)` and
  `Test.Refuses(…)` see the same failures under the same fields.
- **The wire.** A request that carries a generated model is checked by the rules it borrows (400 + field failures).
- **Hints and columns.** Read off the validator's descriptor once per type: `NotEmpty()`/`NotNull()` is
  `[Required]` (the required badge, `aria-required`, NOT NULL), `MaximumLength`/`Length` is `[MaxLength]`/`[StringLength]`
  (`maxlength`/`minlength`, the column's length), `EmailAddress()` is `type="email"`, `InclusiveBetween` and
  `GreaterThanOrEqualTo`/`LessThanOrEqualTo` (and `GreaterThan`/`LessThan` of a whole number) are `min`/`max`,
  `PrecisionScale(p, s)` is `step` and `decimal(p, s)`. A rule under `When`/`Unless` gives no hint. An attribute on the
  same property wins for its column ([RASK182](diagnostics.md#rask182) says when they disagree).
- **Messages.** The catalog's sentences, as above; `WithMessage` and `WithName` win.

A separate `AbstractValidator<Supplier>` class does all of the same, so an existing validator moves over unchanged;
the class and the in-type `Rules` may both be there, and both run. A validator that takes services gets them from the
render or request scope (`RaskValidators.Service<T>`); it states no hints, because building it needs a scope.

A value object may state its rules the same way — they run as it is made, after its base record's own, and never for a
value loaded from a row:

```csharp
public sealed partial record Handle(string Value) : Text(Value, MaxLength: 30)
{
    static void Rules(InlineValidator<Handle> rules) =>
        rules.RuleFor(h => h.Value).Must(v => !v.Contains(' ')).WithMessage("A handle has no spaces.");
}
```

A failure about `Value` is the value's own; one about another member (`Opening(DateOnly? Date, decimal? Quantity)`)
is under that member. A record with its own `Rules()` override and a static `Rules` is [RASK183](diagnostics.md#rask183).

### Three ways to say a rule

The **value object is the recommended, DDD way**: the rule lives in the type, once, and holds wherever a value is made.
DataAnnotations and FluentValidation are the familiar on-ramp — fully supported, so you can start the way you work
today and move a member to a value object when it earns one ([RASK130](diagnostics.md#rask130)'s quick-fix writes the
record from the attribute or the rule).

| Rule | DataAnnotations | FluentValidation | Value object (recommended) | Value object + FluentValidation |
|---|---|---|---|---|
| Filled | `[Required]` | `RuleFor(s => s.Name).NotEmpty()` | `Text(Value)` (filled by default) | `Text(Value)` |
| At most 200 characters | `[StringLength(200)]` | `.MaximumLength(200)` | `Text(Value, MaxLength: 200)` | `Text(Value, MaxLength: 200)` |
| At least 2 characters | `[MinLength(2)]` | `.MinimumLength(2)` | `Text(Value, MinLength: 2)` | `.MinimumLength(2)` in its `Rules` |
| An e-mail address | `[EmailAddress]` | `.EmailAddress()` | `Email(Value)` | `Email(Value)` + `.Must(…)` |
| Between 1 and 5 | `[Range(1, 5)]` | `.InclusiveBetween(1, 5)` | `Whole(Value, Min: 1, Max: 5)` | `.InclusiveBetween(1, 5)` in its `Rules` |
| Two decimals in ten digits | `[Precision(10, 2)]` | `.PrecisionScale(10, 2, false)` | `Number(Value, Precision: 10, Scale: 2)` | `Number(Value, Precision: 10, Scale: 2)` |
| A pattern | `[RegularExpression("^[A-Z]+$")]` | `.Matches("^[A-Z]+$")` | `Text(Value, Pattern: "^[A-Z]+$")` | `.Matches(…)` in its `Rules` |
| Anything else | `IValidatableObject` / a custom attribute | `.Must(…)` / `.MustAsync(…)` | `protected override IEnumerable<Problem> Rules()` | `.Must(…)` in its `Rules` |

### A validator that needs services

A uniqueness rule has to ask something. Declare the dependency on the constructor and it is resolved
from the render scope — the generator reads the constructor and builds the validator for you:

```csharp
public sealed class OrderValidator : AbstractValidator<OrderModel>
{
    public OrderValidator(IProductCatalog catalog) =>
        RuleFor(x => x.Product)
            .MustAsync(async (sku, ct) => await catalog.Exists(sku, ct))
            .WithMessage("No such product.");
}
```

One public constructor is the rule. Several leaves no way to choose, which is
[RASKVAL002](diagnostics.md#raskval002); two validators for one model is
[RASKVAL001](diagnostics.md#raskval001).

### Both passes run, attributes first

A model can carry `[Required]` **and** have an `AbstractValidator<T>`. Both run: DataAnnotations is
the sync stage and the discovered validator the async one, so the existing pipeline order already
puts attributes first, and per-field first-error-wins means an attribute message shadows a
FluentValidation one on the same field. Nothing was reordered to make this work.

### Turning the automatic validators off

The attribute pass and the discovered validator run with nothing declared. An app that writes its rules
inline and wants only those says so once:

```csharp
RaskValidation.AutoValidate = false;          // every form, on any host
app.Configure(c => c.Validation.Off());       // the same switch, from a RaskApp
Form.Model(_model).AutoValidate(false)[ … ]   // this form only
```

Inline `.Validate(…)` rules keep running: the switch only stops what was never written in the app. A rule
a property's type carries and a rule the store owns were written — on the type, on the index — so they
keep running too.
It is one switch for the app, so it also stops the same two passes on a
[dispatched request](validation.md#requests). The global off wins — a form cannot opt back in.

---

## Async validators and the validating indicator

Three ways to validate asynchronously:

1. **Inline async `.Validate(…)`** — `async v => …`, [above](#an-async-rule). The next change cancels
   the check in flight (latest wins).
2. **`IAsyncFieldValidator`** — reach for this when the rule needs DI (an `HttpClient`, a
   repository) or you want to reuse it across forms. Add it to an `EditContext` you own:

   ```csharp
   public sealed class UniqueUsernameValidator : IAsyncFieldValidator
   {
       public async ValueTask ValidateField(EditContext ctx, FieldIdentifier field, CancellationToken ct)
       {
           if (ctx.Model is SignupModel m && field.FieldName == nameof(SignupModel.Username))
           {
               await Task.Delay(400, ct);            // pretend it's an API call
               if (await IsTaken(m.Username))
                   ctx.AddValidationMessage(field, "Already taken.");
           }
       }
       public ValueTask Validate(EditContext c, CancellationToken ct) => default;
   }

   _ctx = new EditContext(_model);
   _ctx.AddValidator(new UniqueUsernameValidator());
   // Form.Model(_model).Context(_ctx)[ … ]
   ```
3. **FluentValidation `MustAsync`** — async rules ride the discovered validator, which is wrapped as an `IAsyncFieldValidator`.
4. **An `AsyncValidationAttribute` or an `IAsyncValidatableObject`** — .NET 11's DataAnnotations, awaited by the form
   with nothing declared: [an async attribute](#an-async-attribute).

Each `await` in a handler triggers a re-render, so a `Validation.Indicator` can surface while a
check is in flight:

```csharp
Validation.Indicator.Template(() => Span.Class("spinner")["Checking…"]).For(() => _model.Username)
```

Place a `Validation.Indicator` yourself beside a raw `Input`, or beside
the kit's fields (`Ui.Input`, `Ui.Textarea`, `Ui.Select`, the checkbox, radio and switch), which draw none.

An `IAsyncFieldValidator` (the username-uniqueness check above) with the validating indicator:

<!-- demo:validation-async -->

Validation can also be driven **programmatically** — `await EditContext.Validate()` and reading `IsValidating`:

<!-- demo:validation-programmatic -->

### `IsValidating` vs `ShouldShowValidatingIndicator`

- `EditContext.IsValidating(field)` / `IsValidatingAny` — the exact "a validator is in flight right
  now" answer. Use it for control flow (e.g. `.Disabled(_ctx.IsValidatingAny)` on a submit button).
- `ShouldShowValidatingIndicator(field)` — `IsValidating` extended with a short **sticky tail**
  (`EditContext.ValidatingStickyMs`, default 200ms). A sub-second check still reads as "showing" for
  the sticky window so the indicator has a footprint screen-readers and Playwright can observe. This
  is what `Validation.Indicator` renders against. The sticky dismissal is a single timer-driven
  re-render at window expiry. Set `ValidatingStickyMs = 0` on a context you own to opt out; it does
  not delay submit or validator completion.

### First-error-wins

The pipeline runs inline → form-level inline → sync `IFieldValidator` → async
`IAsyncFieldValidator`. Once any stage flags a field, later stages stay quiet on that **same** field
— so fixing one error reveals the next rule's message. A validator that throws mid-check surfaces a
generic `"Validation could not be completed."` rather than killing the submit pipeline.

<!-- demo:validation-first-error-wins -->

A **cross-field** rule (form-level `.Validate(…)` feeding the `Validation.Summary`):

<!-- demo:validation-cross-field -->
