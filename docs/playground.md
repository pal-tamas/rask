# Live playground

**[Open the playground ↗](https://pal-tamas.github.io/rask/playground/)** — write Rask component C# in
your browser with a real IDE: **IntelliSense**, **as-you-type diagnostics** (the framework's own RASK
squiggles included, before you ever press Run), and a **gallery of ready-to-run examples**. See it render
live — nothing is sent to a server, the C# is compiled entirely in WebAssembly.

The playground is the `samples/Rask.Example.Playground` app, published to GitHub Pages next to the
[feature showcase](https://pal-tamas.github.io/rask/docs/) (the showcase's navbar links to it).

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

Pressing Run is the only path that Emits and loads an assembly; the diagnostics from that run are shown
both as a panel and as inline [Monaco](https://microsoft.github.io/monaco-editor/) markers.

### IntelliSense and as-you-type diagnostics

The editor is a real IDE, not just a text box. A few seconds after load — once the framework references
finish downloading in the background — a **workspace-backed analysis path** comes alive (the readiness pill
next to the title flips to *IntelliSense ready*):

- **IntelliSense** is Roslyn's own `CompletionService`, so completions know the full BCL + `Rask.Core`
  surface *and* the `Generated.Div(...)` factories the source generator brings into scope — you get the
  terse `Div()[…]` members exactly as you would in a real project.
- **Diagnostics update as you type** — CS errors and Rask's RASK hints squiggle on every edit, not only on
  Run.

Crucially this path **never `Emit`s or `Assembly.Load`s** — it binds and queries only. That's what makes
typing free: only pressing Run loads an assembly (and Mono WebAssembly can't unload one), so as-you-type
analysis can't leak. Monaco talks to it through a pair of static `[JSInvokable]` bridge methods, the same
JS→.NET dispatch the framework's browser wrappers use.

## Writing code

Pick one of the **examples** in the left-hand gallery — **Counter**, **Form + validation** (Rask's built-in
`Form<T>` validation), or a small **Todo app** — as a starting point; **Reset** restores an example's
original code, and **Ctrl/Cmd + Enter** runs.

Define a component named **`Playground`** as the entry point, in a namespace (as in any real Rask project —
that's what lets the generator bring your own components' factories into scope):

```csharp
using Rask.Core;

namespace Demo;

public sealed partial class Playground : Component
{
    private int _count;

    protected override Component? Render() =>
        Div.Class("card")[
            H1["Hello, Rask 👋"],
            P[$"You clicked {_count} times."],
            Button.Class("btn").OnClick(() => _count++)["Click me"]
        ];
}
```

You can declare additional components, use `Context`, callbacks, lifecycle hooks — anything that lives in
`Rask.Core`.

## The guided tutorial

The left pane's **Tutorial** tab is an eight-chapter track that starts at "what is a component" and ends
with a database. Each chapter loads its own code, states its goal and what to notice, and ticks itself off
once you get it to compile — your edits included, since the tick means *it built*, not *you clicked*.

| # | Chapter | What you learn |
|---|---------|----------------|
| 1 | Your first component | `Render()`, the tag factories, the children indexer |
| 2 | State and events | a field, a handler, automatic re-render |
| 3 | Composition and lists | a child component's generated factory, and `Key:` |
| 4 | Forms and validation | `Form<T>`, two-way `Input(() => model.Field)`, `Validate:` |
| 5 | Your first entity | `Entity<Guid>`, a `DbContext`, `EnsureCreated()`, an insert |
| 6 | Query and display | LINQ over a `DbSet`, translated to SQL |
| 7 | Edit and delete | an update, then soft delete and the query filter that hides it |
| 8 | Relationships | a navigation property, saving a graph, `Include()` |

**Chapters 5–8 run real EF Core against a real SQLite database, inside the tab.** Not a mock and not an
in-memory provider: `e_sqlite3` is linked into the published WebAssembly runtime, so `SaveChangesAsync`
writes rows a later `Where(...)` reads back through actual SQL. They use the same
[`Rask.Data`](data.md) conventions a real project uses — `Entity<Guid>`
for identity and audit stamps, `ApplyRaskConventions()` for the soft-delete filter, and the auditing /
soft-delete interceptors — so what you learn here is what you will write on your machine.

Each chapter owns its own database file, because chapters evolve the schema and `EnsureCreated()` does
nothing to a database that already has tables. Chapter 5 keeps what you insert, so pressing Run again
shows your rows still there; chapters 6–8 drop and reseed theirs on every Run, so an experiment always
starts from the same four products. **Reset** restores the chapter's code and clears that chapter's
database — nobody else's.

When you reach the end, [the full tutorial](tutorial/00-overview.md) picks up where this leaves off with
the parts that need a real machine: `rask new`, migrations, background jobs, email, and deployment.

## Limitations

- **First compile is slow** (a one-time multi-MB reference download, then an interpreted Roslyn compile —
  a few seconds). Later compiles reuse the cached references.
- **User code always runs interpreted.** Assemblies loaded at runtime are never AOT-compiled, even in an
  AOT-published app, so the preview runs at interpreter speed.
- **Memory grows per Run.** Mono WebAssembly can't unload an assembly, so each **Run** leaks one — reload
  the tab to reclaim. (As-you-type diagnostics and IntelliSense never Emit or load, so typing is free.)
- **The tutorial's databases are in memory.** A browser has no real filesystem: the chapter databases live
  in the runtime's in-memory filesystem, so at most they last as long as the tab and vanish on reload. That is
  the intent here — see [SQLite in the browser](sqlite.md#sqlite-in-the-browser-wasm) for why you should
  *not* build an app this way.
- **The data chapters need the full build.** Linking `e_sqlite3` means a native relink at publish (the
  `wasm-tools` workload). A build made with `-p:WasmBuildNative=false` ships without the EF Core reference
  set, and marks chapters 5–8 read-only instead of pretending they work.

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
