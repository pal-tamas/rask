# Rask.Site

The one app published to [rask.sh](https://rask.sh): the landing page at `/`, and the guides plus every
live demo at `/docs`. It is a **browser-WASM** Rask app — the browser boots the .NET runtime and renders
and handles events locally, with no server and no WebSocket — and it is built with Rask itself, so the
site is also the framework's largest working example.

It is an app, not a library: `IsPackable=false` and `RaskPublicApiTracked=false`.

## Run

```bash
dotnet build src/Rask.Site -c Debug -m:1          # serially — the WASM asset pipeline races in parallel
dotnet run --project src/Rask.Site -c Debug --no-build -- --urls http://localhost:5050
```

Open <http://localhost:5050>. `curl` only ever sees the boot shell — the app exists once a real browser
boots the runtime. To drive it headlessly and take screenshots, use the `run-rask` skill
(`.claude/skills/run-rask/`), which ships a .NET Playwright driver.

## Publish

```bash
dotnet publish src/Rask.Site -c Release
```

The output is static files under `bin/Release/net10.0-browser/publish/wwwroot`, prerendered
(`<RaskPrerender>true</RaskPrerender>`) and served from the site root. `.github/workflows/pages.yml`
publishes exactly this to rask.sh. The publish must stay free of IL trim warnings — new reflection
needs a `[DynamicallyAccessedMembers]` annotation or a justified suppression.

## Test

```bash
dotnet test tests/Rask.Site.Tests      # page, routing and guide tests (unit)
scripts/run-e2e-local.sh               # the browser E2E suite, tests/Rask.Site.E2E.Tests
```

Every change here needs an E2E test. The E2E suite runs locally, not in CI.

## Layout

| Folder      | Contents                                                                                          |
|-------------|---------------------------------------------------------------------------------------------------|
| `Features/` | One folder per page or demo: `Home/` (the landing page), `Guides/` (the guide pages, `GuideCatalog`, `llms.txt`), and the showcase demos. |
| `Shared/`   | The layouts (`SiteLayout`, `ShowcaseLayout`), the demo registry, code samples and SEO metadata.    |
| `Styles/`   | The site's Tailwind stylesheet.                                                                  |

The guides are the repository's `docs/*.md`, rendered at `/docs/guides/<slug>` from the entries in
`Features/Guides/GuideCatalog.cs`.
