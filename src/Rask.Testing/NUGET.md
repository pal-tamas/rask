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
- **`.TryInvokeAsync(handlerId, json?)`** — dispatch only if the id is still live; returns `false` rather
  than throwing, so you can assert a handler is gone.
- **`.Instance`** — the component object you passed in, for asserting its state directly.
- **`.Render()`** — re-render after mutating external state the component reads.
- **`TestJSRuntime`** — an `IJSRuntime` that records calls and returns canned values, for components that
  inject `IJSRuntime`. Register it in the provider, then assert with `.ArgsFor(id)` / `.Calls` /
  `.CallCount(id)`; configure with `.SetResponse(id, value)` / `.SetException(id, ex)`.
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
