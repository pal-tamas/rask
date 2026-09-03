# Migrating from Blazor

Rask isn't a Blazor replacement so much as a different take on the same problem
space. If you know Blazor, most concepts transfer directly — they just move from
`.razor` markup into plain C#. This guide maps the day-to-day APIs and calls out the
behavioural differences that trip people up.

New to Rask entirely? Start with [getting started](getting-started.md).

## Concept mapping

| Blazor | Rask |
|--------|------|
| `.razor` component (markup + `@code`) | `sealed partial class : Component` with `Render()` returning a tree |
| `RenderFragment` / Razor markup | A chain — `Div.Class("panel")[Span["hi"]]`, children via an indexer |
| `@onclick="Handler"` | `.OnClick(() => ...)` (a plain delegate, set by a chain step) |
| `[Parameter] public T X { get; set; }` | `public T X { get; set; }` — becomes a chain step (required if non-nullable) or an optional setter |
| `EventCallback` / `EventCallback<T>` | **No such type.** A plain delegate prop (`Action`, `Action<T>`, `Func<Task>`, `Func<T,Task>`) |
| `[CascadingParameter]` / `CascadingValue` | `Context.Provide<T>(value)` / `Context.Get<T>()` / `Context.Required<T>()` |
| `@key="x"` | `.Key(x)` — an ordinary chain step |
| `OnInitialized` / `OnInitializedAsync` | `OnMount` / `OnMountAsync` (once per instance) |
| `OnParametersSet` / `OnParametersSetAsync` | `OnPropsChanged` / `OnPropsChangedAsync` |
| `OnAfterRender(firstRender)` / async | `OnRendered(firstRender)` / `OnRenderedAsync` |
| `Dispose` / `DisposeAsync` | implement `IDisposable` / `IAsyncDisposable`; or use `OnUnmount` / `OnUnmountAsync` |
| `NavigationManager` | `Navigator` (event-handler-only) + `RouteState` (current path/params) |
| `@page "/path"` | `[Route("/path")]` on the class — **Rask's `Route`, from `Rask.Core.Routing`**; Blazor's attribute of the same name leaves the page unregistered ([RASK067](diagnostics.md#rask067)) |
| route/query binding | `[RouteParam]` / `[QueryParam]` on a property |
| `<EditForm>` + `InputText`/`InputNumber` | `Form.Model(model).OnValidSubmit(…)` + `Input.Bind(() => model.X)` |
| `<DataAnnotationsValidator>` | drop `DataAnnotationsValidator` inside the `Form` |
| `<AuthorizeView>` (+ `Context="user"` / `@context.User`) | headless `Authorize` — its `.Authorized(user => …)` slot receives the `ClaimsPrincipal`, like `@context.User` |
| `AuthenticationStateProvider` | inject `IUserProvider` and read `.Current` |
| `[Inject] T Svc { get; set; }` | constructor injection — `partial class Page(T svc) : Component` |
| `Component.razor.css` | sibling `{Component}.css` next to `{Component}.cs` |
| `IJSRuntime` | `IJSRuntime` (injected via the constructor) |

## Side-by-side examples

### A parameterised component

```razor
@* Blazor: Greeting.razor *@
<h1>Hello, @Name!</h1>
@code {
    [Parameter] public string Name { get; set; } = "";
}
```

```csharp
// Rask
public sealed partial class Greeting : Component
{
    public required string Name { get; set; }   // non-nullable → a required chain step
    protected override Component? Render() => H1[$"Hello, {Name}!"];
}
// call site: Greeting.Name("Ada")
```

### Event handler + local state

```razor
@* Blazor *@
<button @onclick="() => count++">Count: @count</button>
@code { int count; }
```

```csharp
// Rask — owner re-renders automatically after the handler
Button.OnClick(() => _count++)[$"Count: {_count}"]
```

### Component → parent callback (no `EventCallback`)

This is the biggest API difference. Blazor wraps child events in `EventCallback` so
the parent re-renders. **Rask has no `Callback`/`EventCallback` type** — the child
declares a plain delegate prop, and the chain step that sets it wraps it so invoking it
re-renders the parent that owns the lambda (the lambda's `this`).

```razor
@* Blazor: child *@
<button @onclick="() => OnRate.InvokeAsync(5)">Rate</button>
@code { [Parameter] public EventCallback<int> OnRate { get; set; } }
```

```csharp
// Rask: child declares a plain delegate; parent passes a lambda over its own state
public sealed partial class RatingStars : Component
{
    public Action<int>? OnRate { get; set; }
    protected override Component? Render() =>
        Button.OnClick(() => OnRate?.Invoke(5))["Rate"];
}

// parent — the lambda captures `this`, so invoking OnRate re-renders the parent
RatingStars.OnRate(n => _rating = n)
```

A static method, or a lambda closing over a local instead of `this`, has no
component target, so **no auto re-render fires** — write the lambda inside the
component that should update.

### Cascading value → Context

```razor
@* Blazor *@
<CascadingValue Value="theme"><Component /></CascadingValue>
@* in Component: *@ [CascadingParameter] Theme Theme { get; set; }
```

```csharp
// Rask — provide high, read deep (nearest provider wins, matched by type)
Context.Provide<Theme>(_theme)[ ThemeCard ]
// in any descendant's Render():
var theme = Context.Required<Theme>();   // or Context.Get<T>() (null if absent)
```

### Routing + navigation

```razor
@* Blazor *@
@page "/users/{Id:int}"
@inject NavigationManager Nav
@code { [Parameter] public int Id { get; set; } }
```

```csharp
// Rask
[Route("/users/{id}")]
public sealed partial class UserPage(Navigator nav) : Component   // Navigator via ctor
{
    [RouteParam] public int Id { get; set; }
    [QueryParam] public string? Tab { get; set; }
    // navigate from a handler: nav.NavigateTo(Routes.HomePage()), nav.SetQuery("tab", "x")
}

// type-safe link (generated URL builder) instead of a "/users/42" string:
NavLink.Href(Routes.UserPage(42))["View user"]
```

### Forms

```razor
@* Blazor *@
<EditForm Model="model" OnValidSubmit="Save">
    <DataAnnotationsValidator />
    <InputText @bind-Value="model.Name" />
</EditForm>
```

```csharp
// Rask
Form.Model(_model).OnValidSubmit(m => Save(m))[
    DataAnnotationsValidator,                  // from Rask.Validation.DataAnnotations
    Input.Bind(() => _model.Name),              // input type inferred from the CLR type
    ValidationMessage.For(() => _model.Name).Template(errs => Div.Class("field-error")[errs[0]]),
    Button.Type("submit")["Sign up"]
]
```

`Input`/`Select`/`Textarea` infer their type from the bound property (`string` →
text, `bool` → checkbox, `int` → number, `DateOnly` → date). `ValidationMessage` and
`ValidationSummary` are headless — you chain a `.Template(…)` lambda for the markup. See
[forms](forms.md) for nested models, collections, and async validation.

### Scoped CSS

Identical idea, no association ceremony. Drop a sibling `{Component}.css` next to
`{Component}.cs` and it's auto-globbed, scoped to that type, and hot-reloaded under
`dotnet watch` — Blazor-parity descendant scoping. There is no per-selector opt-out
(no `:global(...)`, no `::deep`): global styles for shell tags like `body`/`html`, brand
palettes, or framework classes go in a plain `wwwroot` stylesheet linked from your App's
`<Head>`.

## Behavioural gotchas

- **Children come through an indexer, not a `Children` parameter.** Write
  `Div[Span["hi"], "there"]`. There is no `ChildContent` step, and `Children`
  is always excluded from the generated chain.

- **The `..` spread fails inside `[...]`.** The C# spread element parses as a
  `Range` inside the indexer. Pass enumerables **directly** instead:

  ```csharp
  Ul[items.Select(i => (Component)Li.Key(i.Id)[i.Name])]   // ✓ pass the sequence
  // Ul[.. items.Select(...)]                            // ✗ parses as Range
  ```

- **F12 / Go-to-Definition on a chain step lands on the *generated* member, not your
  component class.** Stock Roslyn/ReSharper navigate a generated symbol to its
  generated document. Use the IDE's **"Navigate to Type of Symbol"** for a one-action
  jump to the component class. (Every generated member carries a `<see cref>`
  breadcrumb and `[DebuggerStepThrough]`, so hover/Quick-Doc offers a navigable link
  and the debugger steps over the generated code into yours.)

- **Keyed lists matter — and Rask warns when you forget.** Like Blazor's `@key`,
  pass `.Key(…)` on list items so inserts/removes/reorders reconcile by identity
  (preserving focus and input state). A Rask chain in a list-projection
  context (`.Select`/`.SelectMany`, or `.Add` in a loop) without a `.Key(…)` raises
  **RASK022** — keyless items reconcile positionally.

- **`Navigator` is event-handler-only.** Calling it outside a handler throws. Read
  the current route from `RouteState`; mutate it via `Navigator` from a handler.

- **Lifecycle re-fire rules differ.** `OnPropsChanged*` fires on first render and
  whenever a bound prop or route/query param actually changes — a bare
  event-handler re-render does **not** re-fire it. Don't call `StateHasChanged()` in
  `OnUnmount*`. Faulted async hooks log to `Console.Error` and don't re-render (a
  placeholder that never resolves usually means an async hook threw).

## What stays the same

- **DI through the constructor** — exactly like the rest of .NET. Framework services
  (`Navigator`, `RouteState`, `HttpClient`, `IJSRuntime`) inject the same way as your
  own services. (No `[Inject]` properties — a non-nullable settable property would
  become a required chain step instead.)
- **Scoped CSS parity** — sibling `{Component}.css`, descendant-combinator scoping,
  hot reload.
- **`IJSRuntime`** — the same interop surface (`InvokeAsync`, `InvokeVoidAsync`).
  Rask adds element refs (`ElementRef.New()` + a `Ref:` parameter on every element)
  and a sibling `{Component}.ts` convention bundled and dispatched as
  `window.Rask["{TypeName}"]`.
