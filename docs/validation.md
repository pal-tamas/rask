# Validation

Validation is built in. Put `[Required]` on a model, or write an `AbstractValidator<T>` for it, and
the rules run — in a form as the user types, and again on the server before a dispatched request
reaches its handler. There is no package to add for DataAnnotations and nothing to declare anywhere.

> Included in the [`Rask`](../README.md) package — nothing to install. It is **on**; an app that does
> without it says so:
>
> ```csharp
> app.Configure(c => c.Validation.Off());
> ```

## Where it runs

| Where | What runs | Guide |
| --- | --- | --- |
| A `Form<T>` | The model's DataAnnotations attributes, then the `AbstractValidator<T>` for it | [forms-validation.md](forms-validation.md) |
| A dispatched query or command | The request's attributes, then its `AbstractValidator<T>`, then any `IRequestValidator<T>` you registered | below |
| A controller or minimal API endpoint | ASP.NET's own DataAnnotations pass only — an `AbstractValidator<T>` does not run there yet | [what is not covered](#what-is-not-covered-yet) |

The two halves share one validator: an `AbstractValidator<Order>` validates a `Form<Order>` while the
user types **and** an `Order` command when it is dispatched. Write the rules once.

## The two sources

**DataAnnotations** lives in `Rask.Core`, which every host already bundles, so it costs no reference
at all. It covers `[Required]`, `[Range]`, `[StringLength]`, `[EmailAddress]`,
`[RegularExpression]`, custom `ValidationAttribute` subclasses and `IValidatableObject`, across the
whole reachable object graph.

**FluentValidation** is the `Rask.Validation.FluentValidation` package, referenced for you by the
`Rask` package on both the server and the browser. Declaring the validator is the registration — a
generator finds every `AbstractValidator<T>` at compile time and emits a `[ModuleInitializer]`. There
is no assembly scan, which is what lets a WebAssembly app use it and still publish trimmed.

A validator with constructor dependencies is resolved from the scope, so the uniqueness-check
validator — the usual reason to reach for FluentValidation — needs no extra wiring. It is built when
validation runs, not when the form renders, so a rule edited and hot-reloaded takes effect on the next
validation rather than the next page load.

### Two things to know

**Discovery is per assembly.** The generator sees the compilation it runs in, so a validator in a
referenced class library is registered only if that library also has the Rask analyzer payload and a
`Rask.Validation.FluentValidation` reference — which a plain class library does not. Keep validators
beside the app, or register them explicitly:

```csharp
RaskValidators.Register(typeof(Order), _ => new OrderValidator());
```

**A discovered validator makes the form validate asynchronously.** FluentValidation runs async, so
`EditContext.Validate()` — the synchronous overload — throws once a validator exists for the model, and
`ValidateAsync()` must be used instead. This is not new behaviour for a context with async validators;
what is new is that writing an `AbstractValidator<T>` is now enough to put one there. The exception
names the validator that made the context async.

## Requests

Every dispatched request is validated before its handler runs, by a `ValidationBehavior` that
`AddRaskCqrs` registers **outermost**: a request that is not valid should never reach a transaction,
a log line saying it was handled, or the handler.

Rules that fit neither source go in an `IRequestValidator<TRequest>`, which is asynchronous by
construction:

```csharp
public sealed class NoDuplicateOrders(IOrderStore store) : IRequestValidator<PlaceOrder>
{
    public async ValueTask<IReadOnlyList<RequestValidationError>> ValidateAsync(
        PlaceOrder request, CancellationToken ct) =>
        await store.ExistsAsync(request.Reference, ct)
            ? [new RequestValidationError(nameof(PlaceOrder.Reference), "That reference is already used.")]
            : [];
}

builder.Services.AddSingleton<IRequestValidator<PlaceOrder>, NoDuplicateOrders>();
```

Every validator runs and their failures are collected. This is deliberately **not** the form's
first-error-wins: a form gates per field because the user is typing into it, while a caller fixing a
request wants the whole list rather than one problem per round trip.

<a id="rejected"></a>

## A rejected request

In process, a failure reaches the caller as a `RaskValidationException` whose `Errors` are grouped by
field, with the empty key holding rules about the request as a whole.

Over [remote dispatch](cqrs.md#remote-dispatch--a-client-and-a-server-raskcqrsclient--raskcqrsserver)
it becomes **400** `application/problem+json`:

```json
{
  "type": "https://github.com/pal-tamas/rask/blob/main/docs/validation.md#rejected",
  "title": "Validation failed",
  "status": 400,
  "errors": {
    "Product": ["No such product."],
    "Quantity": ["Quantity must be at least 1."]
  }
}
```

The `type` is stable across releases, so it is the right thing for a client to branch on. The client
surfaces it as a `RemoteDispatchException` with `Errors` populated.

Note that this is the one failure whose text crosses the wire. A handler exception is opaque by
default because its message is written for an operator and routinely names tables, paths and
credentials; a validation message is the opposite — it was authored to be shown to whoever sent the
request.

### The browser checks first

On a WebAssembly client the request is validated **before** it is sent, so an invalid command costs a
message rather than a round trip. The server runs the same rules again and remains the authority —
the local check is a convenience, never a control, and a caller that skips it gains nothing.

Catch **both**: a rejection caught in the browser is a `RaskValidationException`, and one the browser
could not evaluate — a `MustAsync` that needs the database — comes back from the server as a
`RemoteDispatchException` whose `Errors` carry the same field map.

Notifications are not validated. `PublishAsync` does not go through the request pipeline, so a rule on
a notification would be enforced nowhere; put it on the command that raises the notification instead.

## What is not covered yet

[HTTP endpoints](api-endpoints.md) are **not** covered by the two passes above — tracked in
[#988](https://github.com/pal-tamas/rask/issues/988).

They are not unvalidated. `AddRaskApi` registers `AddMvcCore().AddDataAnnotations()`, so a
controller's `[Required]` and `[Range]` are enforced by `ModelState` exactly as they are in any
ASP.NET app. What is missing is the other half of what a form and a request get: an
`AbstractValidator<T>` written for a request type does **not** run on a controller action or a minimal
API endpoint, and neither does an async rule.

The intended shape is the platform's own synchronous pass — `ModelState` for controllers, .NET 10's
`AddValidation()` for minimal APIs — plus a Rask asynchronous filter running the discovered validator
and merging into the same 400 shown above, so one client handles a rejection from any seam.

## See also

- [forms-validation.md](forms-validation.md) — inline, per-field, async, and the validating indicator.
- [forms.md](forms.md) — binding and the `EditContext`.
- [cqrs.md](cqrs.md) — dispatch, pipeline behaviors, remote errors.
- [diagnostics.md](diagnostics.md#raskval001) — RASKVAL001, RASKVAL002.
