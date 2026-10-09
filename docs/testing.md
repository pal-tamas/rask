# Testing Rask components

Rask components are plain C# classes that render to HTML, so most behaviour is reachable from fast
in-process unit tests — no browser, no server. This guide covers the test stack, rendering and
asserting on output, driving event handlers, testing forms and validation, the build/test commands,
and when to reach for end-to-end (E2E) tests instead.

> Unit-test first. Add an E2E test only when a unit test genuinely can't reach the path (E2E is
> heavy — see the last section).

---

## 0. The `Rask.Testing` package (start here)

**A new app already has a test.** `rask new Shop` (the `server` and `wasm` templates) writes `Shop.Tests/`
beside the app, listed in `Shop.slnx`, so `dotnet test` is green before you have written anything:

```csharp
public sealed partial class HomePageTests : RaskMarkup
{
    [Fact]
    public void Home_page_greets_the_visitor()
    {
        var page = Page.Render(() => HomePage);

        Assert.Contains("Hello, Rask!", page.Html);
    }
}
```

The class derives from `RaskMarkup`, which is what makes the app's pages reachable by name — `HomePage` —
the same way they are in markup. Add a file beside it per feature. `rask new --no-tests` leaves the project
out.

Reference **`Rask.Testing`** from your test project and you can render a component, invoke its handlers,
and assert on the re-rendered HTML through a small public API — no browser, server, or WebSocket:

```csharp
using Rask.Testing;

public sealed partial class Counter : Component
{
    private int _count;
    protected override Component? Render() =>
        Button.Type(ButtonType.Button).OnClick(() => _count++)[$"Count: {_count}"];
}

[Fact]
public async Task Clicking_increments()
{
    var page = Page.Render(() => Counter);          // renders + wires event handlers
    Assert.Contains("Count: 0", page.Html);

    await page.On("button").Click();               // dispatch the button's click handler, then re-render
    Assert.Contains("Count: 1", page.Html);
}
```

The test class derives from `RaskMarkup`, like the one `rask new` writes, so `Counter` is reachable by name as it
is in markup — a component is built through its chain, never with `new` (RASK014).

- **`Page.Render(component, services?)`** → a `Page<T>`. Pass an `IServiceProvider` when the
  component constructor-injects framework services or your own registrations.
- **`Page.Render(factory, services?)`** — renders the component the factory returns, re-running the
  factory on **every** render so the tree is rebuilt from your current state. Reach for this whenever a
  re-render should see changed props; the `component` overload renders one fixed instance, so a tree you
  build at the call site keeps the values it was built with:

  ```csharp
  var model = new OrderModel();
  var page = Page.Render(() => Form.Model(model)[Input.Bind(() => model.Name)]);

  await page.On("input").Change("Ada");  // the next render rebuilds the form from `model`
  ```

  Returning `null` renders nothing, and drives the component it stops returning through its unmount path.
- **`Page.RenderDocument(app, services?)`** — renders the component the way a host does, with the
  whole document composed around it, so you can assert on the **page**: the doctype, `<html lang>`, the
  `<head>` every mounted component contributed to, `<body class>`. Reach for it only when the page is
  what you're asserting about — `Render` adds no markup of its own, which is what keeps an assertion
  about a component from quietly becoming an assertion about a page.

  ```csharp
  var page = Page.RenderDocument(App, services);
  Assert.StartsWith("<!DOCTYPE html>", page.Html);
  Assert.Contains("<html lang=\"en\">", page.Html);
  Assert.Contains(">My app</title>", page.Html);   // head tags carry a dedupe key attribute
  ```
- **`.Html`** — the current markup. **`.Render()`** re-renders after you mutate external state it reads.
- **`.Shows(text)`** / **`.Shows(html => …)`** — re-renders until the page shows the text (or the markup
  satisfies the condition), waiting up to **`.Patience`** (5 seconds by default); throws a `PageException`
  carrying what the page does show. It is synchronous — no `await`. This is how you test a component that
  **loads asynchronously**: the component is mounted by `Render`, but `OnMount` completes on a
  continuation, so what it loads is not in the markup yet when `Render` returns.

  ```csharp
  var page = Page.Render(() => OrdersPage, services);
  page.Shows("2 orders");                     // rather than a fixed delay

  page.Patience = TimeSpan.FromSeconds(10);   // a slower load
  page.Shows(html => html.Contains("data-state=\"done\""));
  ```

  Both overloads of `Render` fire `OnMount` and, once it has rendered, `OnFirstRender` and `OnRendered` — the component
  renders through the handle, so state it sets after an await reaches the markup on the next render.
- **`.On(selector)`** — the events of the **one** element the selector matches (tag, `#id`, `.class`,
  `[attr]`, `[attr="v"]`, descendant/child combinators, `:has-text("…")`; none or several throws):
  `.Click()`, `.Input("Ada")`, `.Change("42")`, `.Submit(form?)`, `.Files(...)`, and `.Raise(domEvent, json?)`
  for event data the verbs don't carry (`.Raise("keydown", "{\"key\":\"Enter\"}")`). Each dispatches, then
  re-renders and returns the new `Html`.

  ```csharp
  await page.On("#name").Input("Ada");
  await page.On("button[type=\"submit\"]").Click();
  ```
- **`.Invoke(handlerId, json?)`** — dispatch a specific handler by id.
- **`.TryInvoke(handlerId, json?)`** — dispatch only if the id is still live; returns `false` instead of
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
  var grid = Page.Render(() => Ui.DataGrid.Data(rows).RowKey(r => r.Id)[c => [
      c.Field(r => r.Name).Sortable(),
      c.Field(r => r.Total).Sortable(),
  ]]);

  await grid.Invoke(grid.HandlerIds("click")[1]);   // click the second sortable header
  ```

  Re-read the list after every render, for the same reason a single id can't be cached.

### Components that call JavaScript

`TestJSRuntime` is an `IJSRuntime` that records every call and returns what you configure — register it and
assert on the identifier and arguments your component shipped:

```csharp
var js = new TestJSRuntime();
js.SetResponse("raskApi.clipboard.read", "hello");
var services = new ServiceCollection().AddSingleton<IJSRuntime>(js).BuildServiceProvider();

var page = Page.Render(() => Copier, services);
await page.On("button").Click();

Assert.Equal(["hello"], js.ArgsFor("raskApi.clipboard.write"));
```

`.Calls` lists every call in order; `.ArgsFor(id)` is the single-call shorthand, `.CallCount(id)` counts, and
`.SetException(id, ex)` faults one. An unconfigured call returns `default` — the same as a real absent value.

### Components that call web APIs

A component that calls the browser through [`Rask.Web`](web-apis.md) is tested by faking the web object it calls:
the fake answers every chain that starts at it, for the test's own flow, and nothing reaches a browser.

```csharp
using var storage = LocalStorage.Fake();
storage.Returns(s => s.GetItem("theme"), "dark");

var page = Page.Render(() => ThemeToggle, services);
await page.On("button").Click();

Assert.Equal(["getItem", "setItem"], storage.Calls.Select(c => c.Member));
```

Any web object fakes the same way — `Navigator.Clipboard.Fake()`, `Window.MatchMedia(q).Fake()` — and `Raise("change",
new MediaQueryListEvent { Matches = true })` fires an event at the handlers subscribed to it.

### Components that take file uploads

A file input's handler receives `IRaskFile`s the host reads back from the browser, so a test has to supply
that host half. `TestFileBackend` is it — stage the bytes, register it, pick the files:

```csharp
var files = new TestFileBackend();
var picked = files.Add("notes.txt", "hello world", "text/plain");

var page = Page.Render(() => UploadPage, TestServiceProvider.With<IBrowserFileBackend>(files));
await page.On("#picker").Files(picked);

Assert.Equal("notes.txt", page.TextOf("[data-testid=name]"));
```

The handler gets real files: `OpenReadStream()` returns the staged bytes, `Size` is their length, and the
`maxAllowedSize` limit is enforced exactly as the real backends enforce it — so a component that forgot to
raise the limit for a large upload fails here rather than on a real file.

> **Register the backend, or the test proves nothing.** Without one, `FileListReader` hands the handler an
> **empty list** — the handler still fires, and a test that asserts "no crash" passes while exercising the
> empty branch. That silence is why `Rask.Core` now reports it through `RaskDiagnostics`.

`Files` takes either specific files or the whole backend (`Files(files)`) when there is one input.
For a file inside a submitted form, use `FormPayload`, which shapes the payload the way `FormData.Files`
reads it:

```csharp
await page.On("#form").Submit(files.FormPayload("attachment", files.Add("cv.pdf", "…")));
```

`.Staged` lists everything added; `.Released` records what the framework handed back after the handler
returned — the browser hosts drop their client-side references at that point and the server frees its upload
slot, so a component holding a `IRaskFile` past the handler is holding something already gone.

### Handing a component its services

`Page.Render` takes any `IServiceProvider`, and `Rask.Testing` depends on no DI container. `TestServiceProvider`
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
var page = Page.Render(() => Form.Model(model)[
    Input.Bind(() => model.Name),
    Test.EditContextProbe(c => ctx = c)
]);

await page.On("input").Change("Ada");
Assert.True(ctx!.IsModified(new FieldIdentifier(model, nameof(model.Name))));
```

The probe renders no markup of its own. Outside a form it captures nothing — the context is ambient only
within the form's subtree.

`Rask.Core` comes transitively from the app under test (via its `Rask.Server` / `Rask.Wasm` reference),
so a test project only references `Rask.Testing` and the app.

The rest of this guide covers `Rask`'s own in-repo test helpers (`Rask.TestSupport`) and deeper patterns
(forms, validation, DI). For app authors, the `Rask.Testing` API above is the supported surface.

---

## 1. Driving the app the way a person does

`Page.Render` gives you one component and its handler ids. For a test about a **feature** — fill this in,
press that, see what it says — open the app at a URL instead. `Page.Visit` **runs your app**: the test
project references it, and `Visit` boots its own `Program.cs` — its services, its settings, its pages — with a
database of its own for this test, then opens the URL through the real router, route guards included:

```csharp
[Fact]
public async Task An_admin_adds_a_product()
{
    var admin = await User.Create(new() { Email = "ann@example.com", Roles = [RaskRoles.Admin] });
    var page = Page.Visit("/products/new").As(admin);

    await page.Type("Tea").Into("Name");
    await page.Pick("Green").From("Category");
    await page.Check("In stock");
    await page.Click("Save");           // what was typed, picked and ticked goes first, as in a browser

    page.Shows("Saved");
    page.IsAt("/products");
    Assert.Equal(1, await Product.Count());
}

[Fact]
public void A_visitor_is_sent_to_sign_in()
{
    var page = Page.Visit("/products/new");

    page.IsAt("/login");
}
```

- **The app, as written.** Whatever `Program.cs` registers and configures is what the page gets; no service
  collection to build in the test. The app starts the way it does in production — pending migrations are
  applied — but opens no port.
- **A database per test.** Each visit gets fresh database files, so tests never see each other's rows and run
  side by side. The test's own reads and writes — `User.Create(…)`, `await Product.Count()` — reach the same
  database the page does.
- **`.As(user)`** — visit signed in, as a row of your app's user table (or any `ClaimsPrincipal`), exactly
  as that user would be after signing in: `[Authorize]`, `Authorize.Roles(…)` and `Current.UserId` all see them.
  Without it the visitor is signed out, and a guarded page sends them to `/login` (or `/forbidden` when they
  are signed in without the role), as the running app does.

A component library has no app to run; there `Page.Visit(url, services)` routes over the services you pass.

Nothing here names a handler id, a selector or a `data-testid`. A field is found by the text beside it —
its label, then its placeholder, then its `aria-label` — which is the same thing a person looks for, and
the reason a test written this way fails when the *screen* breaks rather than when the markup is
rearranged.

- **`page.Type(text).Into(field)`** / **`page.Pick(option).From(field)`** — type into an input, choose an
  option of a select by its visible text.
- **`page.Check(label)`** / **`page.Uncheck(label)`** — tick or clear a checkbox.
- **`await page.Click(text)`** — press the button or link with that text. `await page.Click("Delete").In("Tea")`
  narrows to one row, section or form when several say the same word.
- **`page.Shows(text)`** — asserts the text is on screen, **waiting** for work still in flight (up to
  `page.Patience`, 5 s) rather than failing on a race. `page.Shows("Saved").In("Toasts")` narrows it.
  **`page.DoesNotShow(text)`** waits for it to go if it is still there.
- **`page.IsAt(path)`** — asserts where the app navigated to.

The handler-id API in section 0 is still there underneath, and the two mix freely: reach for
`page.HandlerId`/`page.Invoke` when what you are testing genuinely is the wiring.

### A flow across batteries

Each battery's static has a fake: `Mail.Fake()`, `Jobs.Fake()`, `Outbox.Fake()`, `Cache.Fake()`, `Files.Fake()`,
`Push.Fake()`. A fake records, sends nothing outside the test, and runs nothing until asked — so a test steps a
flow one stage at a time and stays deterministic:

```csharp
[Fact]
public async Task Placing_an_order_mails_the_customer_a_receipt()
{
    using var outbox = Outbox.Fake();
    using var jobs = Jobs.Fake();
    using var mail = Mail.Fake();
    var page = Page.Visit("/orders/new").As(ann);

    page.Type("Tea").Into("Item");
    await page.Click("Place order");
    await outbox.Run();   // the durable handlers of what was saved run
    await jobs.Run();     // the jobs they enqueued run, through their real handlers

    outbox.Stored<OrderPlaced>().Once();
    jobs.Enqueued<SendOrderReceipt>().Once();
    mail.Sent().To("ann@example.com").Once();
}
```

`Run()` executes each recorded piece of work as the tenant and user who started it, includes work a running
handler enqueues and delayed jobs (`.In`, `.At`), and lets a handler's exception out. `Outbox.Fake()` records
where the outbox is on; without it a durable handler already runs in line.

---

## 2. The test stack

Tests run on **xUnit v3** (`xunit.v3`; every `*.Tests` project is an executable, set once in
`tests/Directory.Build.props`). The `Rask.TestSupport` project (`tests/Rask.TestSupport/`) builds on
`Rask.Testing` and adds only what the shipped package deliberately doesn't have — helpers that call
`Assert` (the package is test-framework-agnostic and stays so), and helpers below the HTML +
handler-dispatch seam it covers. Attribute lookups over an HTML string are `MarkupAssert.Attr(html, name)`,
which compiles the same scanner `Rask.Testing`'s `Page.Attr` uses — there is one scanner.

- **`RenderHarness`** — `Render<T>(component, services)` begins a `LiveRenderContext`, resolves the
  component, and fires `NotifyParameters`; `EmptyServices()` builds an empty `IServiceProvider` for
  components that need no registrations. (`Page.Render`'s default provider resolves *nothing*, by design,
  so the package takes no DI dependency — these are different tools, not duplicates.)
- **`MarkupAssert`** — the asserting/live-payload lookups: `RequireAttr`, `SessionId`,
  `FirstHandlerId(html)` and `FirstHandlerId(byte[] jsonPayload)`.
- **`Stubs`** — `StubComponent` (a live-render root that forwards to the component under test, which
  often can't itself be a root) and `ContextCapture` (captures the ambient `EditContext` during
  render so a test can assert against what a form/validator pushed).

The internal render entry points (`RenderAsLiveRoot`, `TryInvokeHandlerAsync`) are exposed to test
projects through `[InternalsVisibleTo]` on `Rask.Core` (`Rask.Core.Tests`, `Rask.Site.Tests`, the
validation test projects, …).

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
(`RenderHarness.EmptyServices()`, or a project-specific builder like the site suite's
`TestServices.Default(...)` in `tests/Rask.Site.Tests/Infrastructure/`).

---

## 3. Unit-testing HTML output

A tag with behaviour of its own has a file in `tests/Rask.Core.Tests/Components/` (`InputTests.cs`,
`SelectTests.cs`), and the two cases worth writing first are the tag with nothing set and the tag with
everything set, which asserts the **exact attribute order**: `id`, `class`, `style`, `data-*`, then the
tag-specific attributes. Tests pin this with full-string equality, and a test is named as the sentence it
proves.

```csharp
[Fact]
public void Unset_props_render_empty_button_tags() =>
    Assert.Equal("<button></button>", Button.ToHtml());

[Fact]
public void Setting_every_prop_emits_the_base_attributes_before_the_tags_own() =>
    Assert.Equal(
        "<button id=\"go\" class=\"btn\" style=\"color:red\" data-test-id=\"primary\" type=\"submit\" disabled name=\"action\" value=\"save\"></button>",
        Button
            .Type(ButtonType.Submit)
            .Disabled()
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
- Elements are generated from MDN, so a new tag needs a test file only for behaviour written by hand
  (a typed binding, an event). Test files opt out of the `RASK014` "build it with the chain" analyzer with
  `#pragma warning disable RASK014` since they define their own `Component` subclasses.

---

## 4. Driving event handlers

Event handlers are registered against the live context at render time and surface as
`data-rask-on-*` attributes whose value is a handler id. To drive one in a test:

1. `RenderAsLiveRoot()` to get the HTML.
2. Pull the handler id with `MarkupAssert.Attr(html, "data-rask-on-click")` (or `-on-input`, `-on-change`,
   `-on-submit`, `-on-files`).
3. Invoke it with `view.TryInvokeHandlerAsync(id, jsonPayload)`, passing a `JsonElement` payload that
   mirrors what the client sends.

```csharp
var p = new Person { Name = "Ada", Age = 30 };
var view = new StubComponent(() => Form.Model(p)[Input.Bind(() => p.Name)]);
var html = view.RenderAsLiveRoot();

// A bound control writes through its `change` handler unless it says .Live() (then `-on-input`).
var changeId = MarkupAssert.Attr(html, "data-rask-on-change");
Assert.NotNull(changeId);

using var doc = JsonDocument.Parse("{\"value\":\"Bea\"}");
var ok = await view.TryInvokeHandlerAsync(changeId!, doc.RootElement);

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

## 5. Forms and validation

Form tests follow the same render-then-invoke loop. A bound control's value arrives as a `change`, as it
does from a browser ahead of the next action; `page.Type(…).Into(…)` keeps it until the next `page.Click(…)`
for you:

```csharp
[Fact]
public async Task Submit_InvalidModel_CallsOnInvalidSubmit_NotOnSubmit()
{
    var p = new Person { Name = "", Age = 0 };
    var validCalled = 0; var invalidCalled = 0;

    var view = new StubComponent(() => Form.Model(p)
        .OnSubmit(_ => validCalled++)
        .OnInvalidSubmit(_ => invalidCalled++)
        .Validate(m => string.IsNullOrEmpty(m.Name) ? ["Name required"] : [])[
        Input.Bind(() => p.Name), Input.Bind(() => p.Age)
    ]);
    var html = view.RenderAsLiveRoot();

    var submitId = MarkupAssert.Attr(html, "data-rask-on-submit");
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

var view = new StubComponent(() => Form.Model(model).Context(ctx)[Input.Bind(() => model.Username)]);
var html = view.RenderAsLiveRoot();

using var changeDoc = JsonDocument.Parse("{\"value\":\"admin\"}");
await view.TryInvokeHandlerAsync(MarkupAssert.Attr(html, "data-rask-on-change")!, changeDoc.RootElement);

var fid = new FieldIdentifier(model, "Username");
Assert.Equal(new[] { "Already taken." }, ctx.GetValidationMessages(fid));
```

For **async** flows, drive validation across the await boundary with a gated validator: invoke the
handler without awaiting it, assert `ctx.IsValidating(fid)` is `true` mid-flight, release the
validator, then await the dispatch task and assert it flips back to `false`. You can also call
`await ctx.Validate()` directly to exercise the pipeline without going through submit.

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

That renders the root exactly as written — its body content, with no document around it. When the
assertion is about the *page* (the doctype, `<html lang>`, what landed in `<head>`), render the root
through `Page.RenderDocument` instead, which composes the document the way a host does:

```csharp
var html = Page.RenderDocument(App, TestServices.Default(routeState: routeState)).Html;
Assert.StartsWith("<!DOCTYPE html>", html);
```

`tests/Rask.Site.Tests/` shows page tests for routing, lifecycle, forms, uploads, and more.

---

## 6. Build & test commands

```bash
dotnet build
dotnet test                                                   # everything

dotnet test --filter "FullyQualifiedName!~Rask.Site.E2E"  # skip e2e (faster inner loop)
dotnet test --filter FullyQualifiedName~InputTests            # one class
```

---

## 7. When to reach for E2E

Prefer a unit test. The Playwright E2E suite (`tests/Rask.Site.E2E.Tests/`) is heavy — it spins
up a real host and a browser — so reserve it for paths a unit test genuinely can't reach:

- the actual JS transports (WebSocket dispatch on Server; JSImport/JSExport on WASM),
- `rask.js` / `rask.wasm.js` client behaviour (DOM diff application, focus/IDL preservation on keyed
  reorders, scoped-asset delivery),
- real auth handshakes (cookie redeem, WS reconnect after sign-in),
- anything depending on real browser layout (e.g. `VirtualizeModel` scroll windowing).

Everything else — rendering, attribute order, binding, validation, lifecycle ordering, event-handler
dispatch — is faster and more reliable as a unit test.
