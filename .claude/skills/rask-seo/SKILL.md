---
name: rask-seo
description: Search-engine and AI-assistant discoverability for rask.sh and the Rask NuGet packages. Use AUTOMATICALLY on any change that touches src/Rask.Site, docs/**, README.md, NUGET.md, llms.txt, GuideCatalog, or a package's Description/PackageTags — and whenever asked to improve SEO, rank higher on Google, show up in ChatGPT/Claude/Perplexity answers, or audit how rask.sh appears. Keeps page and guide copy, JSON-LD, sitemap lastmod and llms.txt correct, verifies them in the published bundle, and reports the off-site actions only the owner can take.
---

# rask-seo — discoverability for rask.sh

Goal: when a .NET developer searches (or asks an assistant) for something Rask genuinely solves, the
answer is a rask.sh page. Rankings follow **content that matches the query + clean technical signals +
links from elsewhere**. The first two live in this repo and are enforced by tests; the third is the
owner's (see "Off-site" below). Hosting is not a lever — GitHub Pages behind Fastly is fast enough.

**Realistic targets are long-tail queries where Rask is the answer**, not head terms Microsoft and Meta
own ("blazor", "react"). Map every new page to one:

| Query a developer types | Page |
| --- | --- |
| blazor alternative / migrate from blazor | `migration-from-blazor` |
| use MudBlazor / Radzen components without a Blazor circuit | `blazor-components` |
| React / Vue / Svelte components in a C# / .NET app | `islands` (+ `/docs/islands`) |
| CQRS .NET source generator, mediator without reflection | `cqrs` |
| SQLite in production .NET, WAL, Litestream | `sqlite`, `08-production-sqlite` |
| background jobs / outbox / cache .NET without Redis | `jobs`, `outbox`, `cache` |
| Tailwind CSS .NET without npm | `tailwind` |
| Web Push from ASP.NET Core, VAPID | `webpush` |
| PWA in C#, browser API (Geolocation, WebUSB, …) in C# | `pwa`, `apis/*` |
| .NET One Person Framework | `one-person-framework`, `/` |

## What is enforced (and where)

Fix the code, never the test. Each of these failed silently at least once before it was a test.

| Invariant | Where | Test |
| --- | --- | --- |
| Every page: `PageMeta.For(title, description, Routes.X())` — title ≤ 60 ending ` — Rask`, description 110–160, unique, trailing-slash canonical | `src/Rask.Site/Shared/PageMeta.cs` | `PageMetaTests` |
| Every page: ONE JSON-LD `@graph` (WebSite, author, WebPage/TechArticle, BreadcrumbList; SoftwareApplication on `/`) | `Shared/StructuredData.cs` | `PageMetaTests` |
| Every guide: `SearchTitle` (≤ 53 — 60 with the ` — Rask` suffix — and no "Rask") + `Description` — `required`, so a new guide does not compile without them | `Features/Guides/GuideCatalog.cs` | `GuideSearchCopyTests` |
| Guides are articles: `og:type=article`, `article:section`, `article:modified_time` from git, `rel=alternate type=text/markdown` | `GuidePage.cs`, `GuideHistory.cs` | `PageMetaTests`, `GuideHistoryTests` |
| Sitemap `<lastmod>` = the page's `article:modified_time`, never the build time | `src/Rask.Wasm/WasmPrerender.cs` | `WasmPrerenderTests` |
| `/llms.txt`, `/llms-full.txt`, `/docs/guides/{slug}.md` generated at publish, links rewritten to resolve on the site | `Features/Guides/LlmsText.cs`, `Program.cs` | `LlmsTextTests`, `SiteExampleTests` (E2E) |
| In-doc links route to guides (a `../guide.md` link included) and every anchor names a heading | `Shared/Markdown.cs` | `DocsLinkTests`, `GuidesTests` |
| Site identity (title, description, author, repo) stated once | `Shared/SiteIdentity.cs` | — |

## Per change

- **New guide** (a `docs/**/*.md`): add its `GuideEntry` with `SearchTitle` and `Description` written
  from what the doc *actually* says, aimed at one query from the table (add a row if it is new). Give
  the doc a descriptive `# H1`, link it from at least one related guide, and — for a pillar — from a
  home-page card. It reaches the sitemap, `llms.txt` and `llms-full.txt` by itself.
- **New routable page**: `HeadAssets => PageMeta.For(...)` with a generated `Routes.X()`, never a
  literal. A demo target nobody should find gets `Meta.Name("robots").Content("noindex")` instead.
- **Renamed or removed slug/route**: a crawler holds the old URL. Keep it answering (repeat `[Route]`
  — the first is canonical) unless the page is truly gone.
- **Package metadata**: the first sentence of `<Description>` says what it is in searchable words
  (".NET", "C#", "ASP.NET Core", the problem); `<PackageTags>` carries the domain terms.
  `PackageProjectUrl` is `https://rask.sh/` (Directory.Build.props) — do not point it back at GitHub.
- **Copy rules**: sentence case; ".NET"/"C#"/"ASP.NET Core"/"WebAssembly" only where true; the first
  sentence stands alone; no superlatives, no invented features; **never Ruby, Rails or DHH**. Check
  lengths with the tests, not by eye.

## Verify

```bash
# unit (fast)
dotnet test tests/Rask.Site.Tests --filter "FullyQualifiedName~PageMeta|FullyQualifiedName~GuideSearchCopy|FullyQualifiedName~LlmsText|FullyQualifiedName~GuideHistory|FullyQualifiedName~DocsLink|FullyQualifiedName~GuidesTests"
dotnet test tests/Rask.Wasm.Tests --filter FullyQualifiedName~WasmPrerender
# the published bundle — what GitHub Pages will serve
dotnet publish src/Rask.Site -c Release
W=src/Rask.Site/bin/Release/net10.0-browser/publish/wwwroot
grep -c '<lastmod>' $W/sitemap.xml; head -20 $W/llms.txt; head -5 $W/docs/guides/cqrs.md
grep -o 'application/ld[^"]*json' $W/docs/guides/cqrs/index.html   # the '+' is written as &#x2B;
bash scripts/run-e2e-local.sh   # SiteExampleTests fetches the same files over HTTP
```

After `pages.yml` deploys, check the live site the way a crawler does:

```bash
curl -s https://rask.sh/sitemap.xml | grep -o '<loc>[^<]*' | sed 's/<loc>//' \
  | while read u; do curl -s -o /dev/null -w '%{http_code} '"$u"'\n' -A Googlebot "$u"; done | grep -v '^200' || echo 'all 200'
curl -sI https://rask.sh/llms.txt | head -1
```

Then paste a guide URL into Google's Rich Results Test (breadcrumb + article must parse) and run
PageSpeed Insights (the anonymous API quota runs out quickly — use the web UI or a key). Watch Core Web
Vitals: the WebAssembly boot is the likeliest LCP cost, not the host.

## Periodic audit (on request, or when a pillar ships)

Search each query in the table (WebSearch) and note where rask.sh lands; ask an assistant the same
question and see whether Rask is named. A query with no rask.sh result is a copy or content gap — fix
the page's `SearchTitle`/`Description`/H1/intro, or write the missing guide. Compare against what the
top results cover that the guide does not.

## Off-site — report these, never do them unasked (outward-facing)

- Google Search Console + Bing Webmaster Tools: verify rask.sh (DNS TXT at dns24.hu), submit
  `https://rask.sh/sitemap.xml`, watch Coverage and Enhancements (Breadcrumbs).
- Links from where .NET developers look: awesome-dotnet / awesome-blazor lists, the NuGet READMEs
  (`NUGET.md` → rask.sh pages), the GitHub repo homepage + topics, posts on r/dotnet, dev.to, Hacker
  News, .NET newsletters and talks. One real article linking a guide beats any on-page tweak.
- A 1200×630 social card (`og:image`, `twitter:card=summary_large_image`) is the owner's design call —
  `PageMeta` deliberately ships none until a real image exists.

## Don't

Fake `aggregateRating`/reviews in JSON-LD (a manual action); keyword-stuff titles; stamp the build time
as `lastmod`/`dateModified`; block the `.md` twins in robots.txt; give two pages the same title; change
the URL shape without `<RaskSiteTrailingSlash>` (canonicals and sitemap must agree).
