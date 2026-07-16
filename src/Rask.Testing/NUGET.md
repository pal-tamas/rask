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
- **`.ClickAsync(json?)` / `.InvokeAsync(handlerId, json?)`** — dispatch a handler (optionally with a JSON
  event payload like `"{\"value\":\"hi\"}"` for an input) and re-render; returns the new `Html`.
- **`.HandlerId(domEvent)`** — the handler id wired to `"click"`/`"input"`/`"change"`/`"submit"`/…
- **`.Attr(name)`** — the first `name="…"` attribute value in the current `Html`.
- **`.Render()`** — re-render after mutating external state the component reads.

## Install

Reference from your **test project**. `Rask.Core` comes transitively from the app under test (via its
`Rask.Server` / `Rask.Wasm` reference), so you don't reference it directly.

```
dotnet add <YourApp>.Tests package Rask.Testing
```

See the [testing guide](https://github.com/pal-tamas/rask/blob/main/docs/testing.md) for forms,
validation, and DI examples.
