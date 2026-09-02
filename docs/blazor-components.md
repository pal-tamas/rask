# Blazor components

`Rask.Blazor` hosts a **real Blazor component** inside a Rask page — one from a Razor Class Library,
from MudBlazor or Radzen, or any `ComponentBase` you already have. The Razor SDK is neither replaced
nor reconfigured: it compiles `.razor` exactly as it always has, and Rask renders the result.

This is the [islands](islands.md) contract with a different runtime behind it. A `.tsx` is a React
component rendered by React; a `.razor` is a Blazor component rendered by Blazor.

To be exact about what changed and what did not: **you still never write `.razor` to write Rask.** A
`.razor` here is a component you are *hosting* — someone else's, or your own from a class library —
not the way you author a Rask page. The chain is still the only authoring surface.

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
| Rask children inside it | ❌ — a compile error ([RASK062](#an-island-takes-no-children)) |
| `[Inject]` services — `IJSRuntime`, `NavigationManager`, Rask's browser APIs | ✅ (see [services](#services)) |
| `OnAfterRenderAsync` | ✅ — **once**, after the first paint (see [services](#services)) |
| `ElementReference`, and anything that takes one | ❌ — captures are discarded |
| Its own `@onkeydown`, `@onsubmit`, `@onmouseover` | ❌ — see [events](#events) |
| `@bind` writing a value back | ✅ (see [binding](#binding)) |
| WebAssembly, published **trimmed** | ✅ — see [both hosts](#both-hosts) |

## A worked example

End to end, and compiled: the component below is a real `.razor` in the repository, its island is
declared in the test suite, and the output printed here is asserted by
`tests/Rask.Blazor.Tests/DocExampleTests.cs`. A test also pins this document against the file, so the
two cannot drift apart.

**1. The Blazor component**, in a Razor Class Library — an ordinary `.razor`, compiled by the Razor
SDK, with nothing in it that knows Rask exists:

```razor
@using System.Globalization

<div class="price-tag @Tone">
    <strong>@Symbol</strong>
    <span>@Price.ToString("0.00", CultureInfo.InvariantCulture)</span>
</div>

@code {
    [Parameter, EditorRequired]
    public string Symbol { get; set; } = "";

    [Parameter]
    public decimal Price { get; set; }

    [Parameter]
    public string? Tone { get; set; }
}
```

**2. The island** — the whole declaration. Nothing is redeclared; the chain steps are read from the
component's own `[Parameter]`s:

```csharp
public sealed partial class Quote : BlazorComponent<PriceTag>;
```

**3. The services**, once in `Program.cs`:

```csharp
builder.Services.AddRask();
builder.Services.AddRaskBlazor();
```

**4. Use it** anywhere the chain goes — a leaf, a subtree, or a whole page:

```csharp
Div.Class("grid")[
    H1["Watchlist"],
    Quote.Symbol("RASK").Price(12.5m).Tone("up"),
]
```

**What comes back**, in the first HTTP response:

```html
<rask-blazor name="Quote" component="…PriceTag">
  <div class="price-tag up">
    <strong>RASK</strong>
    <span>12.50</span>
  </div>
</rask-blazor>
```

Three things in that are worth naming:

- **`Symbol` opened the chain** because `PriceTag` marked it `[EditorRequired]`. `Price` and `Tone`
  are ordinary optional steps, and omitting `Tone` leaves the component's own default alone rather
  than passing null.
- **The island is a leaf.** `Quote[ … ]` would be a compile error: an island renders the markup of
  the component it hosts and takes no Rask children — see [children](#an-island-takes-no-children).
- **`12.50` is in the first response**, not painted in later. Had `PriceTag` awaited in
  `OnInitializedAsync`, that would be finished too.

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

### Required parameters

A parameter the hosted component marked `[EditorRequired]` becomes a **required** chain step — one
the call site cannot omit, taken before any optional step. Blazor already has a word for
"mandatory", so it maps onto Rask's own and neither framework has to learn the other's idiom.

```csharp
// MudBlazor: [Parameter, EditorRequired] public string Label { get; set; }
Badge.Label("New")              // ✓ required, so it opens the chain
Badge.Label("New").Tone("warn") // ✓ optional steps follow
Badge.Tone("warn")              // ✗ RASK038 — Label is never set
```

Everything not marked stays optional. An `EventCallback` is never required even when marked: "you
must handle this event" is not something the chain can usefully insist on, and an unwired callback is
simply not wired, exactly as for any other Rask component.

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

**Not every event, and the ones that cannot work render nothing rather than pretending.** Rask routes
an inbound event to a handler by the delegate's shape and refuses a mismatch, so an event it cannot
feed gets no attribute at all — a component that looks wired and does nothing on the first click is
the failure this package exists to avoid. What works today:

| | |
|---|---|
| `click`, `focus`, `blur`, `focusin`, `focusout` | ✅ |
| `select`, `invalid`, `reset`, the `drag*` family | ✅ |
| `change`, `input` — including `@bind` | ✅ |
| `keydown`, `keyup`, `submit`, `mouseover`, `wheel`, `paste`, … | ❌ no attribute emitted |

## Binding

`@bind` works, including the write-back. It travels a different channel from the click above, and
that is the whole reason it works: `change` and `input` are deliberately absent from Rask's DOM-event
table, because a value-carrying event goes through Rask's **input** channel instead — the one that
ships the element's value alongside the handler id.

So a bound input renders with `data-rask-on-input` rather than `data-rask-on-change`, the browser
sends the value, Rask hands it to the island as a string, and the island turns it into the
`ChangeEventArgs` the binder `@bind` generated is waiting for.

```razor
<input @bind="Text" />
<p>echo: @Text</p>
```

Typing updates `Text` inside the hosted component and the echo follows, with no circuit involved.

## An island takes no children

An island renders the markup of the component it hosts, and nothing else. Writing children into one is
a **compile error** — [RASK062](diagnostics.md#rask062) — rather than something that binds, compiles
and then silently renders nothing:

```csharp
Chart.Series(_series)[ H2["Revenue"] ]   // RASK062
```

Children would have to cross as a `RenderFragment`, and there is no answer that is right for every
component. The hosted type may have no fragment parameter at all, one under a name only it knows
(`Content`, `Body`), or several of them (`HeaderContent`, `ToolBarContent`, `RowTemplate`). Picking
among those is how an island ends up looking composable while quietly dropping what it was given —
and a silent drop is the one failure this package is built to avoid.

Compose the other way round instead. It costs nothing, and it reads better:

```csharp
Div.Class("rounded-xl border p-4")[
    H2["Revenue"],
    Chart.Series(_series),
    Button.OnClick(Refresh)["Refresh"],
]
```

The Rask markup stays Rask's — ordinary elements, ordinary handlers, an ordinary diff — and the island
stays a leaf that owns exactly the DOM its hosted component wrote.

> The `.tsx`/Lit island holds the same line for the same reason — see
> [islands.md](islands.md#an-island-takes-no-children). One rule, both island families.

## Tailwind

Worth knowing before you write a utility class in a hosted component, because the failure is silent:
**Tailwind only emits the classes it can see**, and it detects sources from the project directory it
runs in. A Razor Class Library is a different project directory, so the app's Tailwind never reads it
— and a component written in `flex gap-4` renders with classes no stylesheet defines.

A `.razor` in the **same project** as the island is already inside the scanned directory and needs
nothing. For a class library, pick one:

**The library compiles its own stylesheet.** Give it a `Styles/app.css` with `@import "tailwindcss";`
and it compiles on its own build, shipping the result as a static web asset at
`_content/<PackageId>/css/app.css` — which the app links once. This is how
`samples/Rask.Example.Shared` already works, so the path is exercised on every build of this
repository rather than only described here.

```csharp
services.AddRaskBlazor(o => o.HeadAssets.Add(
    Link.Rel("stylesheet").Href("_content/MyComponents/css/app.css")));
```

**Or the app scans the library.** Tailwind v4 takes extra sources as a directive, so one stylesheet
can cover both projects — no second compile, no `_content` link:

```css
@import "tailwindcss";
@source "../MyComponents";
```

The first keeps the library self-contained and shippable on its own; the second keeps one stylesheet
for the whole app. Neither is more correct, and a third-party library (MudBlazor, Radzen) needs
neither — it ships its own CSS, which you add through `HeadAssets` the same way.

## Services

Call `AddRaskBlazor()` in `Program.cs`. It registers what a library component will demand — most
notably `NavigationManager`, which many components inject and throw without.

**`[Inject]` works, and resolves out of your app's own container.** The island builds its hosted
component through Blazor's own activator, so anything you registered is available — `IJSRuntime`,
your own services, and Rask's typed browser APIs:

```csharp
@inject IMediaQuery Media

@code {
    private bool _dark;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;
        _dark = await Media.PrefersDarkAsync();
        StateHasChanged();
    }
}
```

This did not work before [#956](https://github.com/pal-tamas/rask/issues/956): the component was
constructed with `new()`, which skips the injection path entirely, so every `[Inject]` property was
null until the first use threw somewhere that named nothing. If you want to control construction
yourself, register an `IComponentActivator` — Blazor's own seam, which the island honours.

**`OnAfterRenderAsync` fires once, after the island's first paint.** Rask drives it, because
`StaticHtmlRenderer` never does. Once rather than after every render, deliberately: a Rask render
walk is not a Blazor render — the island is walked whenever anything on the page changes — so "after
every render" would fire far more often than a `.razor` author expects, for reasons that have nothing
to do with the component. Use `OnParametersSet` to react to later prop changes.

> **Do not call `StateHasChanged` from `OnAfterRenderAsync`.** This is Blazor's own documented trap —
> the hook feeds the render that fires it — and hosted in an island it recurses through the renderer
> rather than merely spinning, because this path is synchronous. Read what you need, assign it, and
> let the single repaint Rask performs after the hook carry it to the page.

`IJSRuntime` only falls back to a runtime that **throws with a message naming the fix** when the app
registered none of its own. Both hosts register a real one, so in practice you get that.

**What still does not work is `ElementReference`.** `BlazorFrameWriter` discards element-reference
captures, so `JS.InvokeVoidAsync("thing.init", elementRef)` — the shape most component libraries use
to bootstrap themselves against their own JavaScript — has no reference to pass. Interop that does
not need one works.

## Both hosts

`Rask.Blazor` targets `net10.0` **and** `net10.0-browser`, and the two share one code path with no
`#if`: a hosted component is rendered to markup in process, which browser-WebAssembly does as readily
as a server. The only difference is where the renderer comes from — the ASP.NET shared framework on
the server, the `Microsoft.AspNetCore.Components.Web` package in the browser.

**Trimmed, which is the default a WASM app publishes with.** That took an annotation rather than a
caveat, and the reason is worth stating: `[Parameter]` discovery reflects inside
`Microsoft.AspNetCore.Components`, on the hosted type — so the trimmer removes the very property
setters `ParameterView` is about to call, and **nothing reports it**. The trim analyser has nothing to
point at (the reflection is in someone else's assembly), the build stays green, no exception is
thrown, and the island renders as an empty `<rask-blazor>` element. `BlazorComponent<TComponent>`
therefore annotates its type parameter:

```csharp
public abstract partial class BlazorComponent<
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] TComponent>
```

which moves the requirement to the one place that knows the concrete type — your island's own
declaration — and the trimmer keeps those properties in whatever assembly the component lives in.
Nothing to configure.

What that does **not** cover is a component library reaching for members by name beyond its
parameters — reflection Rask cannot see and cannot annotate for. If a hosted component from a large
library renders wrong only after publishing, confirm it by publishing once with:

```xml
<PublishTrimmed>false</PublishTrimmed>
```

and, if that is the difference, root the library with a `TrimmerRootAssembly` rather than turning
trimming off for the whole app.

The trimmed path is gated rather than asserted: `samples/Rask.Example.Wasm` hosts a real `.razor` from
`samples/Rask.Example.Razor` on its **Blazor island** page, the showcase publishes trimmed on every
build, and the browser E2E over that page checks the hosted component's *output* — its parameters,
its own `@onclick`, its `@bind` — because an empty island is exactly what a "the element is there"
check would pass on.

It is deliberately **not** in the `Rask` meta-package on either framework. Everything there is
referenced by every app on that framework, and an app that wants nothing to do with Blazor should not
carry its renderer.

## What is not here yet

- **`ElementReference`, and interop that needs one.** Element-reference captures are discarded by the
  frame writer, so a component that hands its own JavaScript a reference to bootstrap against — which
  is how most menus, dialogs and autocompletes work — still renders inert. `[Inject]`,
  `IJSRuntime` and `OnAfterRenderAsync` do work now; see [services](#services).
- **A prop change replaces the island's DOM.** The update ships as a subtree replace rather than a
  fine diff, so scroll position and text selection inside the island are lost when a prop changes.
- **No `RenderFragment` parameter gets a chain step** — not `ChildContent`, not a named one, not a
  templated `RenderFragment<T>`. See [children](#an-island-takes-no-children).
- **Events beyond the set above** emit no attribute, so a hosted `@onkeydown` is inert.
- **Circuit mode.** `BlazorInteractivity.Circuit` is reserved in the enum and not implemented.

## Diagnostics

[RASK061](diagnostics.md#rask061) · [RASK062](diagnostics.md#rask062) · [RASK064](diagnostics.md#rask064) ·
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
