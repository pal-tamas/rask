# Rask documentation

Guides and references for building with Rask. New here? Start with the project
[README](../README.md) for the pitch and a quick demo, then come back for depth.

## Guides

| Guide | What it covers |
|-------|----------------|
| [Getting started](getting-started.md) | Install the templates, scaffold an app, write your first component, add interactivity and a route. |
| [Routing](routing.md) | `[Route]`, route/query params, nested routes, type-safe `Routes.*` URLs, `Navigator`, `RouteState`. |
| [Composition](composition.md) | Children & fragments, callbacks (child→parent), context (provide/consume), `Virtualize`, drag-and-drop. |
| [JS interop](js-interop.md) | Scoped CSS & JS conventions, calling JS via `IJSRuntime`, element refs (`Ref:`), asset delivery. |
| [Forms & validation](forms.md) | Two-way binding, `Form<T>`/`EditContext`, inline / DataAnnotations / FluentValidation / async validators, radio & checkbox groups. |
| [Lifecycle](lifecycle.md) | `OnMount` / `OnPropsChanged` / `OnRendered` / `OnUnmount`, async-hook rules, cancellation, common gotchas. |
| [Data access (EF Core)](data-access.md) | EF Core + SQLite in a Server app: `IDbContextFactory`, loading in the lifecycle, vertical slices, a DDD aggregate + value objects, and the SQLite decimal gotcha. |
| [Authentication](authentication.md) | Production auth: cookie & JWT, Server & WASM, `Authorize`, route guards, Identity / Keycloak / Auth0 / Cognito / Duende. |
| [Testing](testing.md) | Unit-testing components with `Rask.TestSupport`, driving event handlers, when to reach for E2E. |
| [Migrating from Blazor](migration-from-blazor.md) | Concept mapping, behavioural gotchas, and what stays the same. |
| [Building with AI assistants](ai-agents.md) | The `AGENTS.md` / `llms.txt` artifacts that let AI tools scaffold and extend Rask apps. |

## Reference

| Reference | What it covers |
|-----------|----------------|
| [Diagnostics (RASK001–022)](diagnostics.md) | Every analyzer/generator diagnostic, what triggers it, and how to fix it. |
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
