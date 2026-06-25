# Rask documentation

Guides and references for building with Rask. **New to Rask?** Read
[Getting started](getting-started.md) start to finish — it goes from zero to a running, routed,
interactive app. Want the pitch and a quick demo first? See the project [README](../README.md).

## Guides

| Guide | What it covers |
|-------|----------------|
| [Getting started](getting-started.md) | Prerequisites, scaffold an app, a tour of the generated files, your first component, interactivity, routing, and troubleshooting. |
| [Best practices](best-practices.md) | Production patterns and common pitfalls across component design, state, forms, data access, security, accessibility, performance and testing — each linking to the deep dive. |
| [Routing](routing.md) | `[Route]`, route/query params, nested routes, type-safe `Routes.*` URLs, `Navigator`, `RouteState`. |
| [Composition](composition.md) | Children & fragments, callbacks (child→parent), context (provide/consume), `VirtualizeModel`, drag-and-drop. |
| [JS interop](js-interop.md) | Scoped CSS & JS conventions, calling JS via `IJSRuntime`, element refs (`Ref:`), typed browser APIs, asset delivery. |
| [📱 Mobile & PWA](pwa.md) | Build installable, offline mobile apps in C# (WASM): web app manifest, service worker, Web Push (`IWebPush`), `dotnet new rask-wasm --pwa`. |
| [Forms & validation](forms.md) | Two-way binding, `Form<T>`/`EditContext`, inline / DataAnnotations / FluentValidation / async validators, radio & checkbox groups. |
| [Lifecycle](lifecycle.md) | `OnMount` / `OnPropsChanged` / `OnRendered` / `OnUnmount`, async-hook rules, cancellation, common gotchas. |
| [Data access (EF Core)](data-access.md) | EF Core + SQLite in a Server app: `IDbContextFactory`, loading in the lifecycle, vertical slices, a DDD aggregate + value objects, and the SQLite decimal gotcha. |
| [Authentication](authentication.md) | Production auth: cookie & JWT, Server & WASM, `Authorize`, route guards, Identity / Keycloak / Auth0 / Cognito / Duende. |
| [Accessibility](accessibility.md) | Setting ARIA attributes, `Role`/`TabIndex`, and focus on any element; the `Img` alt-text analyzer (RASK023). |
| [Testing](testing.md) | Unit-testing components with `Rask.TestSupport`, driving event handlers, when to reach for E2E. |
| [Migrating from Blazor](migration-from-blazor.md) | Concept mapping, behavioural gotchas, and what stays the same. |
| [Building with AI assistants](ai-agents.md) | The `AGENTS.md` / `llms.txt` artifacts that let AI tools scaffold and extend Rask apps. |

## Reference

| Reference | What it covers |
|-----------|----------------|
| [Diagnostics (RASK001–024)](diagnostics.md) | Every analyzer/generator diagnostic, what triggers it, and how to fix it. |
| [Code analysis](code-analysis.md) | Analyzers, warnings-as-errors, and the per-PR adoption procedure. |

## Contributing

| Doc | What it covers |
|-----|----------------|
| [Development workflow](development-workflow.md) | The format → warnings-as-errors → tests → benchmarks → docs → review → PR gate, CI, nightly, releases. |

## Architecture

| Doc | What it covers |
|-----|----------------|
| [Live rendering & the diff codec](architecture/live-rendering.md) | How the render walk, frame stream, edit-op diff, keyed reconciliation, and the two transports (Server WS / WASM JSImport) work. |

---

The in-repo map for contributors lives in [CLAUDE.md](../CLAUDE.md). Runnable feature demos are
under [`samples/`](../samples).
