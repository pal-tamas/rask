# Forms — validation

Inline, DataAnnotations, FluentValidation, and async validators for Rask forms.

‹ Back to [Forms & validation](forms.md)

## Inline validation (no extra package)

The lightest layer ships in `Rask.Core`. Pass a `Validate:` lambda — per-field or per-form. Both
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

Add the `Rask.Validation.DataAnnotations` package and drop `DataAnnotationsValidator()` inside the
form. It's a real (headless) component that registers an `IFieldValidator` on the form's
`EditContext` via `EditContextScope.Current?.AddValidator(...)` — one declaration covers the whole
reachable model graph. The package adds a global using, so the validator is in scope.

```csharp
public sealed class SignupModel
{
    [Required, StringLength(20, MinimumLength = 3)] public string Username { get; set; } = "";
    [Required, EmailAddress]                        public string Email    { get; set; } = "";
}

Form<SignupModel>(_model, OnValidSubmit: m => Console.WriteLine(m.Username))[
    DataAnnotationsValidator,
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

---

## FluentValidation

Add the `Rask.Validation.FluentValidation` package and drop
`FluentValidationValidator(new MyValidator())` inside the form. It wraps any
`FluentValidation.IValidator` into an `IAsyncFieldValidator` (so async `MustAsync` rules work too).

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
    FluentValidationValidator(new OrderValidator()),
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
3. **FluentValidation `MustAsync`** — async rules ride the same `FluentValidationValidator` wrapper.

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
