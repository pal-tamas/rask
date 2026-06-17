# Testing Rask components

Rask components are plain C# classes that render to HTML, so most behaviour is reachable from fast
in-process unit tests — no browser, no server. This guide covers the test stack, rendering and
asserting on output, driving event handlers, testing forms and validation, the build/test commands,
and when to reach for end-to-end (E2E) tests instead.

> Unit-test first. Add an E2E test only when a unit test genuinely can't reach the path (E2E is
> heavy — see the last section).

---

## 1. The test stack

Tests run on **xUnit**. The `Rask.TestSupport` project (`tests/Rask.TestSupport/`) collects the
helpers that render components and pull values out of the output:

- **`RenderHarness`** — `Render<T>(component, services)` begins a `LiveRenderContext`, resolves the
  component, and fires `NotifyParameters`; `EmptyServices()` builds an empty `IServiceProvider` for
  components that need no registrations.
- **`Markup`** — string helpers over rendered HTML / live payloads: `Attr(html, name)` (or
  `RequireAttr`), `SessionId`, `FirstHandlerId`.
- **`Stubs`** — `StubComponent` (a live-render root that forwards to the component under test, which
  often can't itself be a root) and `ContextCapture` (captures the ambient `EditContext` during
  render so a test can assert against what a form/validator pushed).

The internal render entry points (`RenderAsLiveRoot`, `TryInvokeHandlerAsync`) are exposed to test
projects through `[InternalsVisibleTo]` on `Rask.Core` (`Rask.Core.Tests`,
`Rask.Example.Shared.Tests`, the validation test projects, …).

The `Rask.Core.Tests` project imports the generated factory namespaces as `global using static`, so
tag factories (`Button(...)`, `Div(...)`), form factories (`Form(...)`, `Input(...)`), and
`Rask.TestSupport` are all in scope unqualified.

### Rendering a component

For a standalone component, `ToHtml()` serializes it directly (no live context):

```csharp
Assert.Equal("<button></button>", Button().ToHtml());
```

For anything that needs a live context (event handlers, forms, DI services), wrap it in a
`StubComponent` and call `RenderAsLiveRoot()`:

```csharp
var view = new StubComponent(() => Button(OnClick: () => { })["x"]);
Assert.Equal("<button data-rask-on-click=\"h0\">x</button>", view.RenderAsLiveRoot());
```

`RenderAsLiveRoot(IServiceProvider)` takes a service provider when the component needs DI
(`RenderHarness.EmptyServices()`, or a project-specific builder like the example suite's
`TestServices.Default(...)`).

---

## 2. Unit-testing HTML output

The per-tag convention (`tests/Rask.Core.Tests/Components/{Tag}Tests.cs`) pairs a `Render_NullProps_…`
case with a `Render_AllPropsSet_…` case that asserts the **exact attribute order**: `id`, `class`,
`style`, `data-*`, then the tag-specific attributes. Tests pin this with full-string equality.

```csharp
[Fact]
public void Render_NullProps_ReturnsEmptyButtonTags() =>
    Assert.Equal("<button></button>", Button().ToHtml());

[Fact]
public void Render_AllPropsSet_EmitsBaseThenDerivedAttributesInOrder() =>
    Assert.Equal(
        "<button id=\"go\" class=\"btn\" style=\"color:red\" data-test-id=\"primary\" type=\"submit\" disabled name=\"action\" value=\"save\"></button>",
        Button("submit", true, "action", "save", Id: "go", Class: "btn", Style: "color:red",
            Data: new Dictionary<string, string?> { ["test-id"] = "primary" }).ToHtml());
```

Useful patterns from the suite:

- Boolean HTML attributes emit bare (`disabled`, not `disabled="true"`) when `true`, and are omitted
  when `false`/`null`.
- `Text` HTML-encodes (`Button()["<click>"]` → `&lt;click&gt;`); `Raw(...)` emits verbatim.
- When adding a new tag, add the matching `{Tag}Tests.cs` with both cases (see the project CLAUDE.md
  conventions). Test files opt out of the `RASK014` "use the factory" analyzer with
  `#pragma warning disable RASK014` since they define their own `Component` subclasses.

---

## 3. Driving event handlers

Event handlers are registered against the live context at render time and surface as
`data-rask-on-*` attributes whose value is a handler id. To drive one in a test:

1. `RenderAsLiveRoot()` to get the HTML.
2. Pull the handler id with `Markup.Attr(html, "data-rask-on-click")` (or `-on-input`, `-on-change`,
   `-on-submit`, `-on-files`).
3. Invoke it with `view.TryInvokeHandlerAsync(id, jsonPayload)`, passing a `JsonElement` payload that
   mirrors what the client sends.

```csharp
var p = new Person { Name = "Ada", Age = 30 };
var view = new StubComponent(() => Form(p)[Input(() => p.Name)]);
var html = view.RenderAsLiveRoot();

var inputId = Markup.Attr(html, "data-rask-on-input");
Assert.NotNull(inputId);

using var doc = JsonDocument.Parse("{\"value\":\"Bea\"}");
var ok = await view.TryInvokeHandlerAsync(inputId!, doc.RootElement);

Assert.True(ok);
Assert.Equal("Bea", p.Name);   // bound model was updated
```

Payload shapes by event:

- input / change → `{"value":"…"}`
- submit → `{"form":{"FieldName":"value", …}}`

`TryInvokeHandlerAsync` returns `false` when no handler matches the id.

---

## 4. Forms and validation

Form tests follow the same render-then-invoke loop. Mirror the browser's input→change ordering when
testing per-keystroke vs blur behaviour:

```csharp
[Fact]
public async Task Submit_InvalidModel_CallsOnInvalidSubmit_NotOnValidSubmit()
{
    var p = new Person { Name = "", Age = 0 };
    var validCalled = 0; var invalidCalled = 0;

    var view = new StubComponent(() => Form<Person>(p,
        OnValidSubmit:   _ => validCalled++,
        OnInvalidSubmit: _ => invalidCalled++,
        Validate: m => string.IsNullOrEmpty(m.Name) ? new[] { "Name required" } : Array.Empty<string>())[
        Input(() => p.Name), Input(() => p.Age)
    ]);
    var html = view.RenderAsLiveRoot();

    var submitId = Markup.Attr(html, "data-rask-on-submit");
    using var doc = JsonDocument.Parse("{\"form\":{\"Name\":\"\",\"Age\":\"0\"}}");
    await view.TryInvokeHandlerAsync(submitId!, doc.RootElement);

    Assert.Equal(0, validCalled);
    Assert.Equal(1, invalidCalled);
}
```

For validation state, capture the form's `EditContext` with `ContextCapture` and assert on its
messages / flags directly:

```csharp
var model = new SignupModel { Username = "ada" };
var ctx = new EditContext(model);
ctx.AddValidator(new RejectIfEqualsValidator("admin", "Already taken."));

var view = new StubComponent(() => Form<SignupModel>(model, Context: ctx)[Input(() => model.Username)]);
var html = view.RenderAsLiveRoot();

using var inputDoc  = JsonDocument.Parse("{\"value\":\"admin\"}");
await view.TryInvokeHandlerAsync(Markup.Attr(html, "data-rask-on-input")!,  inputDoc.RootElement);
using var changeDoc = JsonDocument.Parse("{\"value\":\"admin\"}");
await view.TryInvokeHandlerAsync(Markup.Attr(html, "data-rask-on-change")!, changeDoc.RootElement);

var fid = new FieldIdentifier(model, "Username");
Assert.Equal(new[] { "Already taken." }, ctx.GetValidationMessages(fid));
```

For **async** flows, drive validation across the await boundary with a gated validator: invoke the
handler without awaiting it, assert `ctx.IsValidating(fid)` is `true` mid-flight, release the
validator, then await the dispatch task and assert it flips back to `false`. You can also call
`ctx.ValidateAsync()` directly to exercise the pipeline without going through submit.

See `tests/Rask.Core.Tests/Forms/FormBindingTests.cs` and `AsyncFormBindingTests.cs` for the full
set. See [forms.md](forms.md) for the framework-side validation semantics.

### Page-level tests

A page is just a component rendered through the app root with a routed `RouteState` and the services
it needs, then asserted on the resulting HTML:

```csharp
var routeState = new RouteState { Path = "/" };
var html = new App().RenderAsLiveRoot(TestServices.Default(routeState: routeState));
Assert.Contains("Hello, world!", html);
```

`tests/Rask.Example.Shared.Tests/Pages/` shows page tests for routing, lifecycle, forms, uploads, and
more.

---

## 5. Build & test commands

```bash
dotnet build
dotnet test                                                   # everything

dotnet test --filter "FullyQualifiedName!~Rask.Examples.E2E"  # skip e2e (faster inner loop)
dotnet test --filter FullyQualifiedName~ButtonTests           # one class
```

---

## 6. When to reach for E2E

Prefer a unit test. The Playwright E2E suite (`tests/Rask.Examples.E2E.Tests/`) is heavy — it spins
up a real host and a browser — so reserve it for paths a unit test genuinely can't reach:

- the actual JS transports (WebSocket dispatch on Server; JSImport/JSExport on WASM),
- `rask.js` / `rask.wasm.js` client behaviour (DOM diff application, focus/IDL preservation on keyed
  reorders, scoped-asset delivery),
- real auth handshakes (cookie redeem, WS reconnect after sign-in),
- anything depending on real browser layout (e.g. `VirtualizeModel` scroll windowing).

Everything else — rendering, attribute order, binding, validation, lifecycle ordering, event-handler
dispatch — is faster and more reliable as a unit test.
