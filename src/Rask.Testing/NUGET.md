# Rask.Testing

Unit-test [Rask](https://github.com/pal-tamas/rask) components — render a component to HTML, invoke its
event handlers, and assert on the re-rendered markup. No browser, no server, no WebSocket.

```csharp
using Rask.Testing;

public sealed class Counter : Component
{
    private int _count;
    protected override Component? Render() =>
        Button(Type: "button", OnClick: () => _count++)[$"Count: {_count}"];
}

[Fact]
public async Task Clicking_increments()
{
    var page = RaskTest.Render(new Counter());
    Assert.Contains("Count: 0", page.Html);

    await page.ClickAsync();               // dispatch the click handler + re-render
    Assert.Contains("Count: 1", page.Html);
}
```

## API

- **`RaskTest.Render(component, services?)`** → a `RenderedComponent`. Renders the component with its
  event handlers wired; pass an `IServiceProvider` when the component constructor-injects services.
- **`RaskTest.Render(factory, services?)`** — same, but the factory runs on **every** render, so the tree is
  rebuilt from your current state each time. Use it whenever a re-render should see changed props:
  `RaskTest.Render(() => Form(model)[Input(() => model.Name)])`. The `component` overload renders one fixed
  instance, so a tree you build at the call site keeps the values it was built with.
- **`RenderedComponent.Html`** — the current markup, reflecting the latest state.
- **`.WaitForAsync(text | predicate, timeout?)`** — re-renders until the markup contains the text (or the
  predicate accepts it), then returns it; throws with the last markup after 5 seconds by default. Use it
  for a component that loads in `OnMountAsync`: `Render` mounts it, but the load completes on a
  continuation, so the result is not in the markup yet when `Render` returns.
- **`.ClickAsync(json?)` / `.InvokeAsync(handlerId, json?)`** — dispatch a handler (optionally with a JSON
  event payload like `"{\"value\":\"hi\"}"` for an input) and re-render; returns the new `Html`.
- **`.HandlerId(domEvent)`** — the handler id wired to `"click"`/`"input"`/`"change"`/`"submit"`/…
- **`.Attr(name)`** — the first `name="…"` attribute value in the current `Html`.
- **`.HandlerIds(domEvent)`** / **`.Attrs(name)`** — every match, in document order. Index these to target
  one of several same-event elements: `await page.InvokeAsync(page.HandlerIds("click")[1])`.
- **`Markup.Attr(html, name)`** / **`Markup.Attrs(html, name)`** — the same lookups over any HTML string.

### Finding elements

`.Find(selector)` returns the element, so an assertion can say *which one* rather than substring-matching
the whole page — which is brittle against exactly the attribute-order invariant the framework pins.

```csharp
var badge = page.Find("#items li.selected .badge");
Assert.Equal("7", badge.TextContent);

Assert.Equal(["3", "7"], page.FindAll(".badge").Select(b => b.TextContent));
Assert.Equal("7 shipped", page.TextOf("#items li.selected"));   // whitespace collapsed
page.TestId("refresh");                                          // [data-testid="refresh"]
```

`.Find` throws when there is **no** match *and* when there is **more than one** — a test that silently
took the first of several keeps passing after somebody adds a second. Use `.FindAll` when several are the
point, and `.Exists(selector)` for presence.

**The selector is a documented subset**, and anything outside it throws rather than quietly matching
nothing: `tag`, `*`, `#id`, `.class`, `[attr]`, `[attr="v"]`, `[attr^="v"]`, `[attr$="v"]`, `[attr*="v"]`,
`:has-text("…")`, and the descendant and `>` combinators. For anything else, give the element an id or a
`data-*` attribute — the test reads better for it too.

### Driving one element

```csharp
await page.On("#save").ClickAsync();
await page.On("#name").InputAsync("Ada");
await page.On("form#signup").SubmitAsync();
```

`.HandlerId(domEvent)` returns the **first** match in the document and `.HandlerIds` is indexed by
position — so adding an unrelated button above the one under test silently re-points the assertion and the
test keeps passing. `.On(selector)` names the element instead. (It's a handle rather than a
`ClickAsync(selector)` overload because `ClickAsync` already takes a `string`, the JSON payload.)

### Fakes for the things a component needs

- **`TestDownloadSink`** — an `IDownloadSink` that records what a component staged. `Navigator.Download`
  refuses to run without one and tells you to "register a fake"; this is that fake. Assert on
  `.Staged` (`FileName`, `ContentType`, `Bytes`, `.Text`).
- **`TestRoute.At("/search?q=hello%20world")`** — a `RouteState` at a URL, query string parsed and
  decoded, repeated keys kept. `TestRoute.NavigatorFor(state, downloads)` wires the `Navigator`.
  Register the `Navigator` in the provider and event dispatch enters its handler scope, so a component
  that navigates or downloads on click can be unit-tested at all.
- **`CapturingDiagnostics.Install()`** — captures the framework diagnostics raised while it is installed,
  so you can assert that a swallowed fault happened (or that none did). Swallow-and-log is the framework's
  designed behaviour for navigate faults, JS dispatch faults and faulted async lifecycle hooks, and
  without this there is no supported way to see them.
- **`.TryInvokeAsync(handlerId, json?)`** — dispatch only if the id is still live; returns `false` rather
  than throwing, so you can assert a handler is gone.
- **`.Instance`** — the component object you passed in, for asserting its state directly.
- **`.Render()`** — re-render after mutating external state the component reads.
- **`TestJSRuntime`** — an `IJSRuntime` that records calls and returns canned values, for components that
  inject `IJSRuntime`. Register it in the provider, then assert with `.ArgsFor(id)` / `.Calls` /
  `.CallCount(id)`; configure with `.SetResponse(id, value)` / `.SetException(id, ex)`. An unconfigured
  call returns `default`; a call configured with the **wrong type** now throws and names both types,
  rather than also returning `default` — `SetResponse("getCount", 1)` against `InvokeAsync<long>` used to
  hand back `0`, indistinguishable from "not configured".
- **`RaskTest.EditContextProbe(capture)`** — placed inside a `Form`'s children, hands you the form's
  `EditContext` so you can assert validation state (`GetValidationMessages`, `IsModified`, `IsValidating`)
  that never appears in the markup.

## Install

Reference from your **test project**. `Rask.Core` comes transitively from the app under test (via its
`Rask.Server` / `Rask.Wasm` reference), so you don't reference it directly.

```
dotnet add <YourApp>.Tests package Rask.Testing
```

See the [testing guide](https://github.com/pal-tamas/rask/blob/main/docs/testing.md) for forms,
validation, and DI examples.
