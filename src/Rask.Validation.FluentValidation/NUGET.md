# Rask.Validation.FluentValidation

**FluentValidation for Rask forms and requests**, in [Rask](https://rask.sh/), a full-stack .NET web
framework for teams of any size. The `Rask` package references it for you on both the server and the
browser.

- **Writing the validator is the registration.** A generator finds every `AbstractValidator<T>` in the app
  at compile time, and a form asks for the one that validates its model — nothing to declare in the form,
  nothing to wire in `Program.cs`.
- **No assembly scan.** Registration is emitted as a `[ModuleInitializer]`, so a WebAssembly app uses
  FluentValidation and still publishes trimmed.
- **Async rules work like sync ones.** Each validator is wrapped as an `IAsyncFieldValidator`, so a
  `MustAsync` uniqueness check runs per field; a validator with constructor dependencies is resolved from
  the scope.
- Per-keystroke validation of a root-model field runs only that property's rules; submit runs every rule.
- A validator in a referenced class library is registered explicitly:
  `RaskValidators.Register(typeof(Order), _ => new OrderValidator());`

## Install

```bash
dotnet add package Rask.Validation.FluentValidation
```

## Use

```csharp
public sealed class OrderValidator : AbstractValidator<OrderModel>
{
    public OrderValidator()
    {
        RuleFor(x => x.Product).NotEmpty();
        RuleFor(x => x.Quantity).GreaterThanOrEqualTo(1).WithMessage("Quantity must be at least 1.");
    }
}
```

```csharp
Form.Model(_model).OnValidSubmit(m => _submission = "Ordered")[
    Input.Bind(() => _model.Product),
    Validation.Message.Template(errors => Span.Class("error")[errors[0]]).For(() => _model.Product),
    Input.Bind(() => _model.Quantity),
    Validation.Message.Template(errors => Span.Class("error")[errors[0]]).For(() => _model.Quantity),
    Button.Type("submit")["Order"]
]
```

Depends on FluentValidation 12.x.

Guides: [Validation](https://rask.sh/docs/guides/validation) ·
[Forms — validation](https://rask.sh/docs/guides/forms-validation)
