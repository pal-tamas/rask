# Ahead-of-time (AOT) compilation

Rask WASM apps ship on the **Mono interpreter** by default. That is the right trade for most apps —
fast builds, small tooling footprint, no native relink. When you want the browser to run
**AOT-compiled** code instead — IL compiled to WebAssembly ahead of time, for lower startup CPU and
faster steady-state execution — Rask supports it as an **opt-in publish mode**. The framework is
engineered to publish with **zero IL (trim + AOT) warnings** and to need **no runtime code
generation** on its own hot paths, so your AOT bundle is clean and predictable.

## Enable it

Publish with `RaskWasmAot=true` (this flips on `RunAOTCompilation`):

```bash
dotnet publish -c Release -p:RaskWasmAot=true
```

That is the only knob. The default (`RaskWasmAot` unset) keeps the interpreter, so nothing changes
for existing builds. AOT needs the WebAssembly build tools:

```bash
dotnet workload install wasm-tools
```

Expect a substantially longer publish — the AOT step runs an Emscripten relink — and a larger
download in exchange for less work at startup. Measure both for your app before committing to it.

## The one thing to know: mixed mode

`RunAOTCompilation=true` on Mono WASM is **mixed mode** — the interpreter stays present as a
fallback. So dynamic-code constructs still *work* at runtime even under AOT. The value of Rask's
AOT-readiness is therefore not "it would otherwise crash"; it is:

- **A clean, analyzer-enforced bundle** — no trim/AOT warnings, so nothing is silently left to the
  slow interpreter fallback.
- **Faster binding** — the framework resolves route/form values through a reflection-free registry
  instead of runtime generics on the render hot path (see below).
- **Forward-correctness** — the code is written to work even if dynamic code is ever fully
  unavailable.

## Custom value types

Rask binds route params, query params, and form fields from strings via
[`IParsable<T>`](https://learn.microsoft.com/dotnet/api/system.iparsable-1). Every **BCL primitive**
(`int`, `long`, `Guid`, `decimal`, `DateOnly`, `TimeOnly`, `DateTime`, `bool`, …) is handled with no
setup. For your **own** `IParsable<T>` value types:

- **Route / query params** are registered automatically. The `RoutesGenerator` emits a
  `RaskBinding.RegisterParsable<T>()` for every custom `[RouteParam]`/`[QueryParam]` type, so a page
  like `[Route("/products/{sku}")] … [RouteParam] public Sku Sku { get; set; }` just works under AOT.

- **Form-model fields** must be registered once at startup, because the generator can't see types
  reached only through `Bind`/`For` expressions:

  ```csharp
  // Program.cs, before the app runs.
  RaskBinding.RegisterParsable<Money>();   // Money : IParsable<Money>
  ```

  Under the interpreter this is optional (Rask falls back to reflection); under a fully interp-free
  AOT build it is required for the field to bind.

## `IJSRuntime.InvokeAsync<T>` under AOT

`InvokeAsync<T>` deserializes the JS result with `System.Text.Json`. The framework's own browser-API
return types are covered by source-generated JSON contexts. For your **own** `T`, supply a
[`JsonSerializerContext`](https://learn.microsoft.com/dotnet/standard/serialization/system-text-json/source-generation)
and chain it into the options — exactly as Blazor WASM requires under AOT. Primitive `T`
(`string`, `int`, …) needs nothing.

## The continuous AOT gate

You don't need to run a full AOT publish to catch AOT-safety regressions. The framework runtime
(`Rask.Wasm`, `Rask.Core`) builds under `IsAotCompatible`, and the WASM sample under
`EnableAotAnalyzer`, so the **trim + AOT Roslyn analyzers run on every build** under
warnings-as-errors. A newly introduced `RequiresDynamicCode` or trim hazard fails the normal build
long before the slow Emscripten step. The full `-p:RaskWasmAot=true` publish runs in CI nightly as
the end-to-end proof.

## Why binding is faster

`ExpressionAccessor.Parse` — which resolves a `Bind`/`For` target — used to compile a throwaway
lambda (`Expression.Compile()`) on **every render** of **every bound control**, then invoke it once.
It now walks the expression with plain reflection instead. Same result, no per-render lambda
compilation, lower allocations on the form hot path. Route/form value parsing likewise consults the
reflection-free `TypedParserRegistry` first, only falling back to reflection for an unregistered
custom type on the interpreter.

## Limitations

- **Mono WASM only.** This is browser-WASM AOT (`RunAOTCompilation`), not Server NativeAOT.
- **Build cost.** AOT publishes are slow and produce larger bundles; keep it for release/perf builds,
  not the inner loop.
- **Reflection in the DataAnnotations pass.** It reflects over model metadata, which is why
  `Form<TModel>`'s type parameter is `[DynamicallyAccessedMembers]`-annotated and the generated chain
  repeats that annotation — without it the trimmer removes the model's properties and the form
  validates nothing, silently. Prefer FluentValidation (source-generated registration, no scan) or
  inline validators when chasing a maximally lean AOT bundle, or turn the pass off with
  `app.Configure(c => c.Validation.Off())`.

See also: [Forms & validation](forms.md), [JS interop](js-interop.md), [Routing](routing.md), and the
[Mobile & PWA guide](pwa.md) for the broader WASM publish story.
