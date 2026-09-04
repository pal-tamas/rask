# Forms — validation

Inline, DataAnnotations, FluentValidation, and async validators for Rask forms.

‹ Back to [Forms & validation](forms.md)

## Inline validation

The lightest layer. Pass a `Validate:` lambda — per-field or per-form. Both
accept a sync `Func<…, IEnumerable<string>>` or an async
`Func<…, CancellationToken, ValueTask<IEnumerable<string>>>`; overload resolution picks by arity, no
cast. An empty sequence means valid.

```csharp
Form<LoginModel>(_model,
    OnValidSubmit: m => _submission = "Welcome",
    Validate: m => m.Password == m.Confirm ? [] : ["Passwords do not match."])[   // cross-field, at submit
    Input.Bind(() => _model.Email)
        .Validate(v => v.Contains('@') ? [] : ["Email looks wrong."]),             // per-field, per-keystroke
    ValidationMessage.For(() => _model.Email).Template(errs => Div.Class("err")[errs[0]]),
    ValidationSummary.Template(SummaryAlert),
    Button.Type("submit")["Sign in"]
]
```

<!-- demo:validation-inline -->

Per-field `Validate:` produces field-scoped messages and runs on each keystroke after the field is
touched. Form-level `Validate:` runs at submit and attaches messages to the form-level slot
(`FieldIdentifier(model, "")`) — they surface in `ValidationSummary`, never against a specific input.

An inline `Validate:` can also be async (`Func<…, CancellationToken, ValueTask<IEnumerable<string>>>`);
the token cancels the in-flight check on the next keystroke:

<!-- demo:validation-inline-async -->

---

## DataAnnotations

Put the attributes on the model. That is the whole setup — there is no package to add and nothing to
declare in the form. `Form<TModel>` registers the pass itself, and one registration covers the whole
reachable model graph.

```csharp
public sealed class SignupModel
{
    [Required, StringLength(20, MinimumLength = 3)] public string Username { get; set; } = "";
    [Required, EmailAddress]                        public string Email    { get; set; } = "";
}

Form<SignupModel>(_model, OnValidSubmit: m => Console.WriteLine(m.Username))[
    Input.Bind(() => _model.Username),
    ValidationMessage.For(() => _model.Username).Template(errs => Div.Class("err")[errs[0]]),
    Input.Bind(() => _model.Email),
    ValidationMessage.For(() => _model.Email).Template(errs => Div.Class("err")[errs[0]]),
    Button.Type("submit")["Register"]
]
```

<!-- demo:validation-fields -->

Supports `[Required]`, `[EmailAddress]`, `[Range]`, `[StringLength]`, `[RegularExpression]`, custom
`ValidationAttribute` subclasses, and `IValidatableObject`. Unlike the BCL's
`Validator.TryValidateObject`, Rask invokes `IValidatableObject.Validate` even when attribute errors
exist — so attribute and object-level errors surface together (ASP.NET Core MVC parity). The
`ValidationContext` is built with the render-scoped `IServiceProvider`, so custom attributes can call
`ctx.GetService<T>()`.

A `ValidationResult` with empty `MemberNames` lands on the form-level slot (`ValidationSummary`); a
populated one tags the named field.

A custom `ValidationAttribute` (with DI via `ctx.GetService<T>()`):

<!-- demo:validation-custom-attribute -->

`IValidatableObject` runs alongside the attributes, model-level:

<!-- demo:validation-validatable-object -->

### Turning it off

Absence of code is the meaning here: a form that says nothing about validation validates. Only the
deviation is written.

```csharp
Form.Model(_model).AutoValidate(false)[ … ]   // this form only

app.Configure(c => c.Validation.Off());       // the whole app
RaskValidation.AutoValidate = false;          // the same switch, without the Rask package
```

The global off wins — a form cannot opt back in.

---

## FluentValidation

Writing the validator is the registration. A generator finds every `AbstractValidator<T>` in your app
at compile time, and a `Form<T>` asks for the one that validates its model — so there is nothing to
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

Form<OrderModel>(_model, m => _submission = "Ordered")[
    Input.Bind(() => _model.Product),
    ValidationMessage.For(() => _model.Product).Template(errs => Div.Class("err")[errs[0]]),
    Input.Bind(() => _model.Quantity),
    ValidationMessage.For(() => _model.Quantity).Template(errs => Div.Class("err")[errs[0]]),
    Button.Type("submit")["Order"]
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
            .MustAsync(async (sku, ct) => await catalog.ExistsAsync(sku, ct))
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

---

## Async validators and the validating indicator

Three ways to validate asynchronously:

1. **Inline async `Validate:`** — return a `ValueTask<IEnumerable<string>>`. The `CancellationToken`
   cancels the in-flight check on the next keystroke (latest-wins).
2. **`IAsyncFieldValidator`** — reach for this when the rule needs DI (an `HttpClient`, a
   repository) or you want to reuse it across forms. Add it to an `EditContext` you own:

   ```csharp
   public sealed class UniqueUsernameValidator : IAsyncFieldValidator
   {
       public async ValueTask ValidateFieldAsync(EditContext ctx, FieldIdentifier field, CancellationToken ct)
       {
           if (ctx.Model is SignupModel m && field.FieldName == nameof(SignupModel.Username))
           {
               await Task.Delay(400, ct);            // pretend it's an API call
               if (await IsTakenAsync(m.Username))
                   ctx.AddValidationMessage(field, "Already taken.");
           }
       }
       public ValueTask ValidateAsync(EditContext c, CancellationToken ct) => default;
   }

   _ctx = new EditContext(_model);
   _ctx.AddValidator(new UniqueUsernameValidator());
   // Form<…>(_model, Context: _ctx)[ … ]
   ```
3. **FluentValidation `MustAsync`** — async rules ride the discovered validator, which is wrapped as an `IAsyncFieldValidator`.

Each `await` in a handler triggers a re-render, so a `ValidatingIndicator` can surface while a
check is in flight:

```csharp
ValidatingIndicator.For(() => _model.Username).Template(() => Span.Class("spinner")["Checking…"])
```

An `IAsyncFieldValidator` (the username-uniqueness check above) with the validating indicator:

<!-- demo:validation-async -->

Validation can also be driven **programmatically** — `EditContext.Validate()` and reading `IsValidating`:

<!-- demo:validation-programmatic -->

### `IsValidating` vs `ShouldShowValidatingIndicator`

- `EditContext.IsValidating(field)` / `IsValidatingAny` — the exact "a validator is in flight right
  now" answer. Use it for control flow (e.g. `Disabled: _ctx.IsValidatingAny` on a submit button).
- `ShouldShowValidatingIndicator(field)` — `IsValidating` extended with a short **sticky tail**
  (`EditContext.ValidatingStickyMs`, default 200ms). A sub-second check still reads as "showing" for
  the sticky window so the indicator has a footprint screen-readers and Playwright can observe. This
  is what `ValidatingIndicator` renders against. The sticky dismissal is a single timer-driven
  re-render at window expiry. Set `ValidatingStickyMs = 0` on a context you own to opt out; it does
  not delay submit or validator completion.

### First-error-wins

The pipeline runs inline → form-level inline → sync `IFieldValidator` → async
`IAsyncFieldValidator`. Once any stage flags a field, later stages stay quiet on that **same** field
— so fixing one error reveals the next rule's message. A validator that throws mid-check surfaces a
generic `"Validation could not be completed."` rather than killing the submit pipeline.

<!-- demo:validation-first-error-wins -->

A **cross-field** rule (form-level `Validate:` feeding the `ValidationSummary`):

<!-- demo:validation-cross-field -->
