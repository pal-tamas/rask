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

There is no package to add and nothing to register. The rule runs when the field changes, once the
reader has touched it, and again on submit.

**A second `.Validate(…)` adds a rule.** They run in the order written, and a rule is not asked about a
value an earlier one rejected:

```csharp
Input.Bind(() => _model.Name)
    .Validate(DestinationName.Validate)   // required, length, format
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

A field's simple rules — required, length, format — belong to the value, not to one form. Put them in
a value object and name them from the field:

```csharp
public readonly record struct DestinationName(string Value)
{
    public const int MaxLength = 255;

    public static IEnumerable<string> Validate(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            yield return "A destination needs a name.";
        else if (value.Length > MaxLength)
            yield return $"A name is at most {MaxLength} characters.";
        else if (!char.IsLetter(value[0]))
            yield return "A name starts with a letter.";
    }
}

Ui.Input.Bind(() => _model.Name)
    .MaxLength(DestinationName.MaxLength)
    .Validate(DestinationName.Validate)
```

`Validate` takes the method by name, because it has the shape of a rule: the value in, the messages
out. Every form that edits a destination's name asks the same question, the constant sets the input's
`maxlength` too, and the domain can call the same method before it saves.

<!-- demo:validation-value-object -->

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

A field's async rule runs on every change — for a live text field, every keystroke once it is touched.
`.Debounce(…)` or `.Blur()` makes that once per pause, or once on leaving the field (see
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
`Validation.Summary`. With nothing on the page to show it, the submit fails as any other does: the
exception is the `f.Error` of `Form.Model(m)[f => [ … ]]`, and it is reported.

It is not logged as an error: the reader was told, and nothing is broken.

<!-- demo:validation-refused-save -->

### When the rules run

A text field binds on every keystroke, and its rules run with it once the reader has touched the field.
`.Debounce(…)` and `.Blur()` move both to the moment the reader stops:

```csharp
Ui.Input.Bind(() => _model.Name).Label("Destination")
    .Debounce(300.Milliseconds)
    .Validate(DestinationName.Validate)   // runs at the pause
    .Validate(NameIsFree),                // a lookup: only for a name the first rule accepted
Ui.Textarea.Bind(() => _model.Notes).Label("Notes")
    .Blur()                               // runs on leaving the field
    .Validate(notes => notes.Length <= 40 ? [] : ["Keep the notes under 40 characters."])
```

Both rules of the first field answer in the one round trip the pause makes. The message under a field
goes the moment the reader starts correcting it, Enter and Save send what was typed before they submit,
and a lookup still running when Save is pressed is run again and waited for. The steps themselves are
described under [Bind timing](forms.md#bind-timing).

<!-- demo:validation-bind-timing -->

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

Per-keystroke validation on a root-model field scopes FluentValidation to that single property
(`MemberNameValidatorSelector`, fast path); submit runs every rule. FluentValidation's own
`Cascade(CascadeMode.Stop)` mirrors Rask's first-error-wins gating.

An async `MustAsync` rule rides the same wrapper:

<!-- demo:validation-fluent-async -->

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

Inline `.Validate(…)` rules keep running: the switch only stops what was never written in the form.
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

Each `await` in a handler triggers a re-render, so a `Validation.Indicator` can surface while a
check is in flight:

```csharp
Validation.Indicator.Template(() => Span.Class("spinner")["Checking…"]).For(() => _model.Username)
```

A field built on `UiFormField<T>` renders this for you, as a small spinner with an announced "Checking…"
under the control, next to its own validation message; none of the kit's own fields is built on it any more.
Place a `Validation.Indicator` yourself beside a raw `Input`, beside
the kit's Flux-drawn fields (`Ui.Input`, `Ui.Textarea`, `Ui.Select`, the checkbox, radio and switch), which draw none, or when a `UiFormField<T>`
opts out with `ShowValidating(false)`.

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
