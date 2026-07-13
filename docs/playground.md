# Live playground

**[Open the playground ↗](https://pal-tamas.github.io/rask/playground/)** — write Rask component C# in
your browser and see it render live, with the framework's own diagnostics as inline squiggles. Nothing is
sent to a server: the C# is compiled entirely in WebAssembly.

The playground is the `samples/Rask.Example.Playground` app, published to GitHub Pages next to the
[feature showcase](https://pal-tamas.github.io/rask/) (the showcase's navbar links to it).

## How it works

Rask components are plain C# — there is no Razor step — so a full compile is just:

```
your C#  →  run the Rask source generator  →  Roslyn CSharpCompilation  →  Emit  →  Assembly.Load  →  render
```

All of that runs in the browser, on the Mono WebAssembly runtime:

1. **References.** The app ships untrimmed, so the assemblies under `_framework/` are complete PE images.
   On first Run it downloads them (a few MB, once) and hands them to Roslyn as `MetadataReference`s — the
   browser has no filesystem, so `MetadataReference.CreateFromImage(bytes)` is used instead of
   `CreateFromFile`.
2. **Compile.** `CSharpGeneratorDriver` runs the **Rask `ComponentFactoryGenerator`** over your code (so
   the `Generated.Div(...)` factories and the `global using static …Generated;` are emitted — you write the
   same terse `Div()[…]` you would in a real project), then `Emit` produces an assembly. Rask's analyzers
   (RASK001–032) run as a second, display-only pass so their diagnostics can surface in the editor.
3. **Render.** The emitted assembly is loaded, the entry component instantiated, and mounted **as a child
   of the playground's own component tree** inside an `ErrorBoundary`. Because it shares the playground's
   live session, your component's event handlers, state and live diffing all work — it's a real mini-app,
   not a static snapshot.

Compiler and analyzer diagnostics are shown both as a panel and as inline
[Monaco](https://microsoft.github.io/monaco-editor/) markers.

## Writing code

Define a component named **`Playground`** as the entry point, in a namespace (as in any real Rask project —
that's what lets the generator bring your own components' factories into scope):

```csharp
using Rask.Core;

namespace Demo;

public sealed class Playground : Component
{
    private int _count;

    protected override Component? Render() =>
        Div(Class: "card")[
            H1()["Hello, Rask 👋"],
            P()[$"You clicked {_count} times."],
            Button(Class: "btn", OnClick: () => _count++)["Click me"]
        ];
}
```

You can declare additional components, use `Context`, callbacks, lifecycle hooks — anything that lives in
`Rask.Core`.

## Limitations

- **First compile is slow** (a one-time multi-MB reference download, then an interpreted Roslyn compile —
  a few seconds). Later compiles reuse the cached references.
- **User code always runs interpreted.** Assemblies loaded at runtime are never AOT-compiled, even in an
  AOT-published app, so the preview runs at interpreter speed.
- **Memory grows per compile.** Mono WebAssembly can't unload an assembly, so each Run leaks one — reload
  the tab to reclaim.

The [Monaco](https://microsoft.github.io/monaco-editor/) editor is vendored under
`wwwroot/lib/monaco/` (self-contained, works offline); if it ever fails to initialize the app falls back
to a plain textarea and compiling/running still work.

## Running it yourself

```bash
dotnet run --project samples/Rask.Example.Playground
```

To reproduce the GitHub Pages sub-path build locally:

```bash
dotnet publish samples/Rask.Example.Playground -c Release -p:RaskPathBase=/rask/playground
```
