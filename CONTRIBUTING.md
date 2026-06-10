# Contributing to Rask

Thanks for your interest in Rask — a C# component framework (Blazor-like) with a Roslyn
factory generator, scoped CSS/JS, routing, and a live diff runtime over WebSockets
(Server) or `JSImport`/`JSExport` (WASM).

## Prerequisites

- .NET 10 SDK (`net10.0`; `net10.0-browser` for the WASM projects).
- For the E2E suite: Playwright browsers (`pwsh tests/Rask.Examples.E2E.Tests/bin/.../playwright.ps1 install`).

## Build & test loop

```bash
dotnet build
dotnet test

# Faster inner loop — skip the heavy Playwright E2E suite:
dotnet test --filter "FullyQualifiedName!~Rask.Examples.E2E"

# A single class:
dotnet test --filter FullyQualifiedName~SessionUploadStoreTests

# Run a sample app:
dotnet run --project samples/Rask.Example.Server
dotnet run --project samples/Rask.Example.Wasm.Host
```

The WASM trimming path is load-bearing: `samples/Rask.Example.Wasm` must
`dotnet publish -c Release` with **zero IL trim warnings**. Any new reflection there needs
a `[DynamicallyAccessedMembers]` annotation or a justified `[UnconditionalSuppressMessage]`.

## Testing expectations

- **Unit-test first.** Every bug fix and new feature gets a unit test; reach for E2E only
  when a unit test genuinely can't reach the path (the Playwright suite is heavy).
- New tests mirror the layout and style of the sibling `+ Tests` project.
- `Highlight_DeepLinkToCodeSamplePage_HighlightsOnFirstPaint` is a known first-paint flake
  — rerun before assuming a regression.

## Repository layout

| Path | What lives there |
|------|------------------|
| `src/Rask.Core/` | Rendering, live diff codec, routing, lifecycle, scoped CSS/JS, primitives. |
| `src/Rask.Generators/` | Roslyn factory/route generators and analyzers (RASK001–022). |
| `src/Rask.Server/`, `src/Rask.Wasm/`, `src/Rask.Wasm.Hosting/` | The three hosts. |
| `src/Rask.Templates/` | `dotnet new` templates (`rask-server`, `rask-wasm`, `rask-wasm-hosted`). |
| `samples/` | Runnable feature showcases. | 
| `tests/`, `benchmarks/` | Test suites and render hot-path baselines. |

Most `src/` projects have a sibling `+ Tests` project. Deeper rationale lives in
[`docs/`](docs/README.md) and the [architecture notes](docs/architecture/live-rendering.md).

## Conventions

- **Adding an HTML tag:** add `src/Rask.Core/Components/{Tag}.cs`
  (`sealed class {Tag} : Element`, `TagName` override, `WriteAttributes` calling `base`
  then `AppendAttr` per attribute; void elements set `SelfClosing => true`) plus a
  `tests/Rask.Core.Tests/Components/{Tag}Tests.cs` asserting exact attribute order
  (id, class, style, data-*, then tag-specific). The factory is generated automatically.
- **Don't `new` a `Component`** outside `Rask.Core` — use the generated factory (RASK014).
- Diagnostics RASK001–022 are documented in [docs/diagnostics.md](docs/diagnostics.md);
  the analyzer descriptors are the source of truth.

## Commits & pull requests

- Keep PRs focused; include tests; ensure `dotnet build` and `dotnet test` are green.
- **Do not** append `Co-Authored-By` or `Generated-with` footers to commits or PR
  descriptions.
- Add a note to [`CHANGELOG.md`](CHANGELOG.md) under `[Unreleased]` for user-visible changes.
