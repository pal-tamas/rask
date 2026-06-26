# AGENTS.md — building this app with an AI assistant

This is a **Rask** app (a C# component framework for .NET 10). This file tells AI coding
assistants the conventions so generated code compiles and runs. Full docs:
https://github.com/pal-tamas/rask/tree/main/docs

## Mental model
- Components are **plain C# classes** deriving from `Component`. Override `RenderResult Render()`
  and return a tree of HTML built with **generated factory methods** — no `.razor`, no JSX.
- The **same component code** runs server-rendered (live diff over WebSockets) or on WASM.

## Writing components — the rules that matter
- **Use factories, never `new`** for components: `Div(...)`, `Button(OnClick: ...)`, `Counter(...)`.
  Constructing a component with `new` outside the framework is a compile error (RASK014).
- **Children go through the indexer**, not a constructor arg: `Div()[Span()["hi"], "text"]`.
  A bare `string` becomes a text node. Pass a list directly for collections: `Ul()[items]`.
  `..` spread does **not** work inside `[...]`.
- **Props are factory parameters.** A nullable prop is optional; a non-nullable prop with no
  initializer is **required**. Declare HTML attributes nullable (`bool? Disabled`) to keep them optional.
- **A page/root component must render the full shell**: `Fragment()[Doctype(), Html(...)[Head(...), Body(...)]]`
  (RASK021). The framework injects its runtime `<script>` automatically — don't add one.
- **Text vs raw:** `Text("..")` / a bare string HTML-encodes; `Raw("..")` is verbatim (XSS risk — avoid for user input).
- **Accessibility:** set ARIA via the `Aria` dictionary on any element — `Button(Aria: new() { ["label"] = "Close" })`
  renders `aria-label="Close"`. `Role:` / `TabIndex:` are typed params. `Img` needs `Alt:` (or `Alt: ""`
  for decorative images) or RASK023 warns. See `docs/accessibility.md`.

## Routing & lifecycle
- Route with an attribute: `[Route("/users/{id:int}")]` + `[RouteParam] public int Id { get; set; }`
  (or `[QueryParam]`). Nested routes: `[ParentRoute(typeof(Parent))]` + `Outlet()`.
- Lifecycle hooks: `OnMount`/`OnMountAsync` (once), `OnPropsChanged*` (on bound-prop/route change),
  `OnRendered(bool firstRender)`, `OnUnmount*`. Navigate only from event handlers via injected `Navigator`.
- **Inject services (`HttpClient`, `Navigator`, `IJSRuntime`, the typed browser APIs
  `IBrowserStorage`/`ICookies`/`IClipboard`/`IGeolocation`/`IPermissions`/`IShare`/`IVibration`/`IPageVisibility`/`INavigatorInfo`/`IWebPush`/`INotifications`/`IBadge`/`IWakeLock`/`IScreenOrientation`, your own) through the constructor**,
  not as settable properties (a non-nullable settable property becomes a required factory param).

## Events, scoped CSS/JS, forms, auth
- **Events / parent callbacks** are plain delegate props: child declares `Action<int>? OnRate`,
  invokes `OnRate?.Invoke(n)`; parent passes `OnRate: n => _x = n`. Invoking re-renders the parent.
- **Scoped CSS/JS:** put `MyComponent.css` / `MyComponent.js` next to `MyComponent.cs`; auto-scoped.
- **Forms:** two-way bind with `Input.Bound(() => model.Name)`; build choice controls by implementing `IFormControl<T>` (the generator synthesizes the bound + controlled factories) or with the public `ExpressionAccessor`/`BindingHelpers`/`EditContext.RegisterFieldValidator` API in `Rask.Core.Forms` (the Rask sample's `CheckboxGroup`/`RadioGroup`/`MultiSelect` are copyable examples).
- **Auth:** gate by injecting `IUserProvider` and reading `.Current` (a never-null `ClaimsPrincipal`), or the `Authorize(...)` component (its `Authorized: user => …` slot is handed the principal; static content uses the `[ … ]` indexer);
  `[Authorize]`/`[AllowAnonymous]` on a page. Configure on ASP.NET's own `AddCookie`/`AddJwtBearer`.

## Build & run
```bash
dotnet run        # then open the printed URL
dotnet test       # if the project has tests
```
If you hit a `RASKxxx` compile error, see https://github.com/pal-tamas/rask/blob/main/docs/diagnostics.md
