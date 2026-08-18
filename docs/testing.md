# Testing Rask components

Rask components are plain C# classes that render to HTML, so most behaviour is reachable from fast
in-process unit tests — no browser, no server. This guide covers the test stack, rendering and
asserting on output, driving event handlers, testing forms and validation, the build/test commands,
and when to reach for end-to-end (E2E) tests instead.

> Unit-test first. Add an E2E test only when a unit test genuinely can't reach the path (E2E is
> heavy — see the last section).

---

## 0. The `Rask.Testing` package (start here)

Reference **`Rask.Testing`** from your test project and you can render a component, invoke its handlers,
and assert on the re-rendered HTML through a small public API — no browser, server, or WebSocket:

```csharp
using Rask.Testing;

public sealed partial class Counter : Component
{
    private int _count;
    protected override Component? Render() =>
        Button.Type("button").OnClick(() => _count++)[$"Count: {_count}"];
}

[Fact]
public async Task Clicking_increments()
{
    var page = RaskTest.Render(new Counter());     // renders + wires event handlers
    Assert.Contains("Count: 0", page.Html);

    await page.ClickAsync();                        // dispatch the click handler, then re-render
    Assert.Contains("Count: 1", page.Html);
}
```

- **`RaskTest.Render(component, services?)`** → a `RenderedComponent`. Pass an `IServiceProvider` when the
  component constructor-injects framework services or your own registrations.
- **`RaskTest.Render(factory, services?)`** — renders the component the factory returns, re-running the
  factory on **every** render so the tree is rebuilt from your current state. Reach for this whenever a
  re-render should see changed props; the `component` overload renders one fixed instance, so a tree you
  build at the call site keeps the values it was built with:

  ```csharp
  var model = new OrderModel();
  var page = RaskTest.Render(() => Form.Model(model)[Input.Bind(() => model.Name)]);

  await page.InputAsync("{\"value\":\"Ada\"}");   // the next render rebuilds the form from `model`
  ```

  Returning `null` renders nothing, and drives the component it stops returning through its unmount path.
- **`RaskTest.RenderDocument(app, services?)`** — renders the component the way a host does, with the
  whole document composed around it, so you can assert on the **page**: the doctype, `<html lang>`, the
  `<head>` every mounted component contributed to, `<body class>`. Reach for it only when the page is
  what you're asserting about — `Render` adds no markup of its own, which is what keeps an assertion
  about a component from quietly becoming an assertion about a page.

  ```csharp
  var page = RaskTest.RenderDocument(new App, services);
  Assert.StartsWith("<!DOCTYPE html>", page.Html);
  Assert.Contains("<html lang=\"en\">", page.Html);
  Assert.Contains(">My app</title>", page.Html);   // head tags carry a dedupe key attribute
  ```
- **`.Html`** — the current markup. **`.Render()`** re-renders after you mutate external state it reads.
- **`.WaitForAsync(text | predicate, timeout?)`** — re-renders until the markup contains the text (or the
  predicate accepts it) and returns it; throws a `TimeoutException` carrying the last markup after 5
  seconds by default. This is how you test a component that **loads asynchronously**: the component is
  mounted by `Render`, but `OnMountAsync` completes on a continuation, so what it loads is not in the
  markup yet when `Render` returns.

  ```csharp
  var page = RaskTest.Render(new OrdersPage(store), services);
  await page.WaitForAsync("2 orders");        // rather than a fixed delay
  ```

  Both overloads of `Render` fire `OnMount`, start `OnMountAsync`, and fire `OnRendered` — the component
  renders through the handle, so state it sets after an await reaches the markup on the next render.
- **`.ClickAsync(json?)` / `.InputAsync(json?)` / `.ChangeAsync(json?)` / `.SubmitAsync(json?)`** — dispatch
  the **first** element wired to that event (optionally with a JSON event payload, e.g.
  `"{\"value\":\"hi\"}"` for an input), then re-render; returns the new `Html`.
- **`.InvokeAsync(handlerId, json?)`** — dispatch a specific handler by id.
- **`.TryInvokeAsync(handlerId, json?)`** — dispatch only if the id is still live; returns `false` instead of
  throwing. Use it to assert a handler is **gone** (a removed element, a disposed subtree).
- **`.Instance`** — the component object you passed to `Render(component)`, so you can assert its own state
  rather than parsing it back out of the markup. It stays the same object for the handle's lifetime.
- **`.HandlerId(domEvent)`** / **`.Attr(name)`** — read a handler id / attribute off the current `Html`.
  A handler id belongs to the component that rendered it and survives a re-render, but not that element
  leaving the tree or its component rendering a different set of handlers — so read one from the current
  `Html` right before invoking rather than reusing a captured id.
- **`.HandlerIds(domEvent)`** / **`.Attrs(name)`** — every match, in document order. This is how you target
  one of several same-event elements — a grid's sort headers, a list's row buttons:

  ```csharp
  var grid = RaskTest.Render(() => BsDataGrid<Row>(Data: rows, Columns: columns));

  await grid.InvokeAsync(grid.HandlerIds("click")[1]);   // click the second sortable header
  ```

  Re-read the list after every render, for the same reason a single id can't be cached.
- **`Markup.Attr(html, name)`** / **`Markup.Attrs(html, name)`** — the same lookups over any HTML string you
  hold, rather than over a `RenderedComponent` (e.g. markup lifted out of a live payload).

### Components that call JavaScript

`TestJSRuntime` is an `IJSRuntime` that records every call and returns what you configure — register it and
assert on the identifier and arguments your component shipped:

```csharp
var js = new TestJSRuntime();
js.SetResponse("raskApi.clipboard.read", "hello");
var services = new ServiceCollection().AddSingleton<IJSRuntime>(js).BuildServiceProvider();

var page = RaskTest.Render(new Copier(), services);
await page.ClickAsync();

Assert.Equal(["hello"], js.ArgsFor("raskApi.clipboard.write"));
```

`.Calls` lists every call in order; `.ArgsFor(id)` is the single-call shorthand, `.CallCount(id)` counts, and
`.SetException(id, ex)` faults one. An unconfigured call returns `default` — the same as a real absent value.

### Components that take file uploads

A file input's handler receives `RaskFile`s the host reads back from the browser, so a test has to supply
that host half. `TestFileBackend` is it — stage the bytes, register it, pick the files:

```csharp
var files = new TestFileBackend();
var picked = files.Add("notes.txt", "hello world", "text/plain");

var page = RaskTest.Render(new UploadPage(), TestServiceProvider.With<IBrowserFileBackend>(files));
await page.On("#picker").FilesAsync(picked);

Assert.Equal("notes.txt", page.TextOf("[data-testid=name]"));
```

The handler gets real files: `OpenReadStream()` returns the staged bytes, `Size` is their length, and the
`maxAllowedSize` limit is enforced exactly as the real backends enforce it — so a component that forgot to
raise the limit for a large upload fails here rather than on a real file.

> **Register the backend, or the test proves nothing.** Without one, `FileListReader` hands the handler an
> **empty list** — the handler still fires, and a test that asserts "no crash" passes while exercising the
> empty branch. That silence is why `Rask.Core` now reports it through `RaskDiagnostics`.

`FilesAsync` takes either specific files or the whole backend (`FilesAsync(files)`) when there is one input.
For a file inside a submitted form, use `FormPayload`, which shapes the payload the way `FormData.Files`
reads it:

```csharp
await page.On("#form").SubmitAsync(files.FormPayload("attachment", files.Add("cv.pdf", "…")));
```

`.Staged` lists everything added; `.Released` records what the framework handed back after the handler
returned — the browser hosts drop their client-side references at that point and the server frees its upload
slot, so a component holding a `RaskFile` past the handler is holding something already gone.

### Handing a component its services

`RaskTest.Render` takes any `IServiceProvider`, and `Rask.Testing` depends on no DI container. `TestServiceProvider`
is the one-liner for the common case of one or two services:

```csharp
var services = new TestServiceProvider()
    .Add<IBrowserFileBackend>(files)
    .Add<IDownloadSink>(downloads);
```

Registrations are by exact type with no lifetimes or scopes — whatever you put in is what comes out. When a
test needs more than that, build a real container and pass its provider instead.

### Forms: asserting validation state

Validation state (messages, `IsModified`, `IsValidating`) never reaches the markup, so reach the form's
`EditContext` with a probe placed **inside** the form's children:

```csharp
EditContext? ctx = null;
var page = RaskTest.Render(() => Form.Model(model)[
    Input.Bind(() => model.Name),
    RaskTest.EditContextProbe(c => ctx = c)
]);

await page.InputAsync("{\"value\":\"Ada\"}");
Assert.True(ctx!.IsModified(new FieldIdentifier(model, nameof(model.Name))));
```

The probe renders no markup of its own. Outside a form it captures nothing — the context is ambient only
within the form's subtree.

`Rask.Core` comes transitively from the app under test (via its `Rask.Server` / `Rask.Wasm` reference),
so a test project only references `Rask.Testing` and the app.

The rest of this guide covers `Rask`'s own in-repo test helpers (`Rask.TestSupport`) and deeper patterns
(forms, validation, DI). For app authors, the `Rask.Testing` API above is the supported surface.

---

## 1. The test stack

Tests run on **xUnit**. The `Rask.TestSupport` project (`tests/Rask.TestSupport/`) builds on
`Rask.Testing` and adds only what the shipped package deliberately doesn't have — helpers that call
`Assert` (the package is test-framework-agnostic and stays so), and helpers below the HTML +
handler-dispatch seam it covers. Plain attribute lookups are `Markup.Attr(html, name)` from
`Rask.Testing` itself; there is one scanner, and it's the shipped one.

- **`RenderHarness`** — `Render<T>(component, services)` begins a `LiveRenderContext`, resolves the
  component, and fires `NotifyParameters`; `EmptyServices()` builds an empty `IServiceProvider` for
  components that need no registrations. (`RaskTest`'s default provider resolves *nothing*, by design,
  so the package takes no DI dependency — these are different tools, not duplicates.)
- **`MarkupAssert`** — the asserting/live-payload lookups: `RequireAttr`, `SessionId`,
  `FirstHandlerId(html)` and `FirstHandlerId(byte[] jsonPayload)`.
- **`Stubs`** — `StubComponent` (a live-render root that forwards to the component under test, which
  often can't itself be a root) and `ContextCapture` (captures the ambient `EditContext` during
  render so a test can assert against what a form/validator pushed).

The internal render entry points (`RenderAsLiveRoot`, `TryInvokeHandlerAsync`) are exposed to test
projects through `[InternalsVisibleTo]` on `Rask.Core` (`Rask.Core.Tests`,
`Rask.Example.Shared.Tests`, the validation test projects, …).

The `Rask.Core.Tests` project is a markup host, so the generator injects the chain entries into it and
tag entries (`Button`, `Div`), form entries (`Form`, `Input`), and
`Rask.TestSupport` are all in scope unqualified.

### Rendering a component

For a standalone component, `ToHtml()` serializes it directly (no live context):

```csharp
Assert.Equal("<button></button>", Button.ToHtml());
```

For anything that needs a live context (event handlers, forms, DI services), wrap it in a
`StubComponent` and call `RenderAsLiveRoot()`:

```csharp
var view = new StubComponent(() => Button.OnClick(() => { })["x"]);
Assert.Equal("<button data-rask-on-click=\"h0\">x</button>", view.RenderAsLiveRoot());
```

`RenderAsLiveRoot(IServiceProvider)` takes a service provider when the component needs DI
(`RenderHarness.EmptyServices()`, or a project-specific builder like the example suite's
`TestServiceProvider.Default(...)`).

---

## 2. Unit-testing HTML output

The per-tag convention (`tests/Rask.Core.Tests/Components/{Tag}Tests.cs`) pairs a `Render_NullProps_…`
case with a `Render_AllPropsSet_…` case that asserts the **exact attribute order**: `id`, `class`,
`style`, `data-*`, then the tag-specific attributes. Tests pin this with full-string equality.

```csharp
[Fact]
public void Render_NullProps_ReturnsEmptyButtonTags() =>
    Assert.Equal("<button></button>", Button.ToHtml());

[Fact]
public void Render_AllPropsSet_EmitsBaseThenDerivedAttributesInOrder() =>
    Assert.Equal(
        "<button id=\"go\" class=\"btn\" style=\"color:red\" data-test-id=\"primary\" type=\"submit\" disabled name=\"action\" value=\"save\"></button>",
        Button
            .Type("submit")
            .Disabled(true)
            .Name("action")
            .Value("save")
            .Id("go")
            .Class("btn")
            .Style("color:red")
            .Data("test-id", "primary")
            .ToHtml());
```

Useful patterns from the suite:

- Boolean HTML attributes emit bare (`disabled`, not `disabled="true"`) when `true`, and are omitted
  when `false`/`null`.
- `Text` HTML-encodes (`Button["<click>"]` → `&lt;click&gt;`); `Raw(...)` emits verbatim.
- When adding a new tag, add the matching `{Tag}Tests.cs` with both cases (see the project CLAUDE.md
  conventions). Test files opt out of the `RASK014` "build it with the chain" analyzer with
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
var view = new StubComponent(() => Form.Model(p)[Input.Bind(() => p.Name)]);
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

`TryInvokeHandlerAsync` returns `false` when no handler matches the id — and, when the payload carries
the client's `"type"` field, when that event cannot feed the handler the id resolved to (an `"input"`
frame landing on a parameterless `OnClick`). A component reuses its own handler slots when it renders a
different set of handlers, so a frame that outlived its render could otherwise run whatever now occupies
one. A payload with no `"type"` — the
shape these examples use — makes no claim, so it dispatches on the id alone as before.

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
        Input.Bind(() => p.Name), Input.Bind(() => p.Age)
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

var view = new StubComponent(() => Form<SignupModel>(model, Context: ctx)[Input.Bind(() => model.Username)]);
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
var html = new App.RenderAsLiveRoot(TestServiceProvider.Default(routeState: routeState));
Assert.Contains("Hello, world!", html);
```

That renders the root exactly as written — its body content, with no document around it. When the
assertion is about the *page* (the doctype, `<html lang>`, what landed in `<head>`), render the root
through `RaskTest.RenderDocument` instead, which composes the document the way a host does:

```csharp
var html = RaskTest.RenderDocument(new App, TestServiceProvider.Default(routeState: routeState)).Html;
Assert.StartsWith("<!DOCTYPE html>", html);
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
