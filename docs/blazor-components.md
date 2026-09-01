# Blazor components

`Rask.Blazor` hosts a **real Blazor component** inside a Rask page — one from a Razor Class Library,
from MudBlazor or Radzen, or any `ComponentBase` you already have. The Razor SDK is neither replaced
nor reconfigured: it compiles `.razor` exactly as it always has, and Rask renders the result.

This is the [islands](islands.md) contract with a different runtime behind it. A `.tsx` is a React
component rendered by React; a `.razor` is a Blazor component rendered by Blazor.

```csharp
public sealed partial class Chart : BlazorComponent<MudChart> { }
```

```csharp
Div.Class("grid")[
    H1["Revenue"],
    Chart.ChartSeries(_series).Width("100%"),
    Button.OnClick(Refresh)["Refresh"],     // ordinary Rask, right beside it
]
```

## What works, and what does not

Read this table before anything else — it is the whole shape of the feature.

| | Hosted Blazor component |
|---|---|
| Renders, styled, in the **first HTTP response** | ✅ |
| `OnInitialized`, `OnInitializedAsync`, `OnParametersSet`, `BuildRenderTree` | ✅ |
| Reacts to a Rask prop change, keeping its own state | ✅ |
| Its own `@onclick` / `EventCallback` firing from the browser | ✅ (see [events](#events)) |
| Rask children inside it, with working handlers | ✅ |
| `OnAfterRender`, `IJSRuntime`, `ElementReference` | ❌ |
| `@bind` writing a value back | ❌ (see [limits](#what-is-not-here-yet)) |
| WebAssembly | ❌ — server only, by construction |

## Declaring one

Derive a `partial` class from `BlazorComponent<T>`. The base class *is* the declaration — there is no
attribute — and `T` names the component being hosted.

```csharp
public sealed partial class Chart : BlazorComponent<MudChart> { }
```

**You do not redeclare its parameters.** A Blazor component already states its surface, so the chain
steps are read from its own `[Parameter]` properties. `MudChart` declaring
`[Parameter] public List<ChartSeries> ChartSeries { get; set; }` is what makes
`Chart.ChartSeries(...)` a step.

The type argument is how the hosted component is named — not a sibling file, the way a `.tsx` island
pairs by filename. That is deliberate: the common case has no `.razor` in your project at all, only a
referenced type.

### Renaming a step

When the hosted parameter's name is not what you want to write at the call site — or collides with a
chain entry of the same name, which is what happens for parameters called `Title`, `Label`, `Form` or
`Select` — name it explicitly:

```csharp
public sealed partial class Chart : BlazorComponent<MudChart>
{
    [BlazorParameter("ChartSeries")]
    public List<ChartSeries>? Series { get; set; }
}
```

A property you declare yourself always wins over the generated one.

### Where the `.razor` has to live

For parameters to be checked at compile time, the hosted component must come from a **referenced**
project or package. A `.razor` in the *same* project as the island is produced by the Razor source
generator, and one source generator never sees another's output — so Rask's generator cannot read its
parameters. The island still works; it just is not verified, and says so through
[RASK066](diagnostics.md#rask066).

Move the `.razor` into a Razor Class Library and reference it. Hosting MudBlazor or Radzen — packages,
therefore referenced — is fully checked with no warning.

## It is in the first response

The component is rendered on the server, during the page render, and its markup is in the HTML that
is sent. A hosted component that awaits in `OnInitializedAsync` has **finished** before the response
goes out rather than appearing a frame later:

```csharp
protected override async Task OnInitializedAsync()
{
    Rows = await _api.LoadAsync();   // done before the page is sent
}
```

That works because the island does its rendering in `OnPropsChangedAsync`, whose task is registered
in the page's quiescence scope — Rask renders, waits for outstanding work, and renders again, sending
the settled wave. See [lifecycle](lifecycle.md).

## Parameters cross as C#, not JSON

Unlike a `.tsx` island, whose props are serialized, parameters here are passed as **live CLR
objects**. `List<ChartSeries>`, a `Func<>`, your own domain type — all cross intact, and there is no
wire vocabulary to violate.

One rule worth knowing, because it is the opposite of the islands feature's: **a nullable property
that is null omits its parameter entirely** rather than passing null. `ParameterView` is
authoritative, so passing null would *overwrite* the hosted component's own default. Not specifying a
value means the component keeps whatever it would have used.

## Events

The hosted component's own event handlers work, and **no Blazor circuit is involved** — no SignalR,
no `blazor.web.js`, no second connection.

Blazor assigns a real handler id to every `@onclick` even in a static render; its own HTML writer
simply discards them. Rask walks the render tree instead and writes its own `data-rask-on-*`
attribute for each, so a click goes through the delegated listener already in the page, travels the
WebSocket that is already open, and is dispatched back into Blazor. It is the same channel the React
and Lit islands use for their callbacks.

```csharp
Table.Rows(_rows).OnRowClick(row => _selected = row)   // fires
```

## Rask children keep working

Children placed inside an island cross as the hosted component's `ChildContent`, and because Rask
delegates events from `document`, **their handlers stay live**:

```csharp
MudCard.Title("Revenue")[
    Chart.ChartSeries(_series),
    Button.OnClick(Refresh)["Refresh"],   // a real Rask handler
]
```

This is strictly better than a `.tsx` island, whose slot content is placed once at mount and then goes
dead. The useful shape is to let the Blazor component be chrome and keep the interactive parts in
Rask.

## Services

Call `AddRaskBlazor()` in `Program.cs`. It registers what a library component will demand of its
constructor — most notably `NavigationManager`, which many components inject and throw without.

`IJSRuntime` is registered as a runtime that **throws with a message naming the fix**, rather than a
silent no-op: a component calling into JavaScript in a statically rendered island has hit a real
capability gap, and a no-op would leave it looking correct while being subtly wrong.

## Server only

`Rask.Blazor` targets `net10.0` only, so a `net10.0-browser` project cannot reference it — restore
fails before any trimmer runs. This is enforced by the package graph rather than by a suppression that
could rot.

The reason is not fixable from Rask's side: `[Parameter]` discovery reflects inside
`Microsoft.AspNetCore.Components`, on types Rask does not own, and no component library is
trim-annotated. `samples/Rask.Example.Wasm` publishes with `TrimMode=full` and warnings as errors, and
there is no honest way to suppress what a hosted component would produce.

## What is not here yet

- **`@bind` writing back.** Rask's DOM handlers carry no payload, so a value-carrying event has
  nowhere to put the value. Read-only display works; two-way binding does not.
- **`OnAfterRender`, `IJSRuntime`, `ElementReference`.** These need a browser-side renderer.
  A component whose behaviour *is* JavaScript — a menu, a dialog, an autocomplete — renders inert.
- **A prop change replaces the island's DOM.** The update ships as a subtree replace rather than a
  fine diff, so scroll position and text selection inside the island are lost when a prop changes.
- **Templated parameters** (`RenderFragment<T>`) get no chain step.
- **Circuit mode.** `BlazorInteractivity.Circuit` is reserved in the enum and not implemented.

## Diagnostics

[RASK061](diagnostics.md#rask061) · [RASK064](diagnostics.md#rask064) ·
[RASK066](diagnostics.md#rask066)

## Why `.razor` is not compiled into the chain

A reasonable thing to want: lower `.razor` into Rask's own chain, so a Razor-authored component is an
ordinary Rask component with the full live diff and WebAssembly support. It was investigated and
rejected on facts, not taste:

- **Razor's syntax layer is `internal` in every shipped version** — `RazorSyntaxTree.Root` returns a
  type no third party can name. There is no public way to walk a parsed Razor document.
- **The .NET 10 SDK's Razor compiler is closed.** It exposes neither the intermediate document nor a
  way to register a pass that would receive one. Its 23 `InternalsVisibleTo` friends — `rzc`, Blazor's
  source generator, the VS and VS Code Razor extensions — reach those instead.
- **There is no open version to pin.** The last published `Microsoft.AspNetCore.Razor.Language` is
  6.0.36, a .NET 6-era package out of support since November 2024.

Hosting the SDK's own output costs nothing that can rot, and gains every Razor feature for free —
including ones a hand-written front end would have taken years to reach.
