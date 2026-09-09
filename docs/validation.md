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
| A controller action or a minimal API endpoint | The bound body's attributes, then the `AbstractValidator<T>` for it — asynchronous rules included | [HTTP endpoints](#http-endpoints) |

All three share one validator: an `AbstractValidator<Order>` validates a `Form<Order>` while the user
types, an `Order` command when it is dispatched, **and** an `Order` posted to `/api/orders`. Write the
rules once.

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

## HTTP endpoints

An [endpoint](api-endpoints.md) runs the same two passes, with nothing declared. Put `[Required]` on the
body type, or write an `AbstractValidator<T>` for it, and a controller action and a minimal API both
reject an invalid request with the [400 shown above](#a-rejected-request) — the same document, from the same
`type`, that a rejected dispatch sends, so one client-side handler covers every seam.

```csharp
public sealed class NewOrder
{
    [Required] public string? Reference { get; set; }
    [Range(1, 100)] public int Quantity { get; set; }
}

public sealed class NewOrderValidator : AbstractValidator<NewOrder>
{
    public NewOrderValidator(IOrderStore store) =>
        RuleFor(o => o.Reference)
            .MustAsync(async (reference, ct) => !await store.ExistsAsync(reference!, ct))
            .WithMessage("That reference is already used.");
}
```

That validator now runs in three places: in a `Form<NewOrder>` as the user types, on a `NewOrder`
command when it is dispatched, and on both of these:

```csharp
[HttpPost("")]
public ActionResult<Order> Place(NewOrder body) => …;      // a controller action

app.MapEndpoints(e =>
    e.MapPost("/api/orders", (NewOrder body, AppDb db) => …));   // a minimal API
```

**The asynchronous rule is the point.** MVC's `ModelState` and `Validator.TryValidateObject` are both
synchronous, so a `MustAsync` — the uniqueness check, the usual reason to reach for FluentValidation at
all — could never ride the platform's own pass. Rask adds an asynchronous filter beside it: an action
filter for controllers, an endpoint filter for minimal APIs.

Only what the **caller** sent is validated. An injected service is left alone — a `DbContext` bound as
an endpoint parameter is the container's, not the request's, and walking its object graph would be both
wrong and slow.

### Where the filter reaches

A controller is covered wherever it lives; MVC runs its filters for every action.

A minimal API is covered when it is mapped through `app.MapEndpoints(e => …)`, which is where an app
writes them. ASP.NET has no such thing as a global endpoint filter — a convention reaches only what is
mapped into the group carrying it — so an endpoint mapped somewhere else asks for the convention by
name:

```csharp
var app = raskApp.Build<App>();
app.MapPost("/api/orders", (NewOrder body) => …).RequireRaskValidation();
```

A host assembled by hand, without `RaskApp`, adds the services half itself:

```csharp
builder.Services.AddRaskApiValidation();
```

### What turning it off does, and does not do

`app.Configure(c => c.Validation.Off())` stops the Rask pass everywhere, endpoints included: the
discovered `AbstractValidator<T>` no longer runs on a controller action or a minimal API.

It deliberately leaves **ASP.NET's own** behaviour alone. `AddRaskApi` registers
`AddMvcCore().AddDataAnnotations()`, so a controller's `[Required]` and `[Range]` are still enforced by
`ModelState` exactly as in any ASP.NET app — dropping that would silently start accepting bodies the
endpoint used to reject, which is worse than a heavier registration. What changes is only the shape of
the answer: with validation on, the rejection is the Rask problem document; with it off, it is MVC's
own `ValidationProblemDetails`.

### On the client

A generated [API client](api-endpoints.md#the-typed-client) surfaces the rejection as an `ApiException`
whose `Errors` hold the same field map, keyed the same way, that `RemoteDispatchException.Errors`
carries for a rejected dispatch:

```csharp
catch (ApiException ex) when (ex.Errors is { } errors)
{
    foreach (var (field, messages) in errors) { … }
}
```

## See also

- [forms-validation.md](forms-validation.md) — inline, per-field, async, and the validating indicator.
- [forms.md](forms.md) — binding and the `EditContext`.
- [cqrs.md](cqrs.md) — dispatch, pipeline behaviors, remote errors.
- [diagnostics.md](diagnostics.md#raskval001) — RASKVAL001, RASKVAL002.
