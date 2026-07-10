using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Rask.Core.Browser;
using Rask.Core.Live;
using Rask.Example.Server;
using Rask.Example.Shared;
using Rask.Server;
using Rask.Server.Diagnostics;

// CodeSample reads demo sources embedded as raksrc/{leaf}. The Server-only PWA demo (ServerPwaDemo)
// lives in this app assembly, not Rask.Example.Shared, so register it with EmbeddedSource.
EmbeddedSource.RegisterAssembly(System.Reflection.Assembly.GetExecutingAssembly());

// The framework default is LiveDiffMode.Auto, so `services.AddRask()` already ships the diff codec
// out of the box. The RASK_DIFF_MODE env var lets the Playwright suite (and curious developers) flip
// modes without recompiling — useful for diff-vs-morph A/B debugging. DiffMode is per-host now (it
// rides the LiveSessionStore), so it is passed through AddRask rather than a process-global static.
LiveDiffMode? envDiffMode =
    Environment.GetEnvironmentVariable("RASK_DIFF_MODE") is { } diffModeName
    && Enum.TryParse<LiveDiffMode>(diffModeName, true, out var parsed)
        ? parsed
        : null;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRask(configure: o =>
{
    if (envDiffMode is { } mode)
    {
        o.DiffMode = mode;
    }
});
// Opt into PWA on the Server host: serves the manifest + service worker, emits the manifest link and
// auto-registers the SW into the server-rendered <head>, so this showcase is an installable PWA. It is
// installable + push-capable but NOT an offline app (offline navigations show wwwroot/offline.html) —
// see the Server PWA showcase page and docs/pwa.md.
builder.Services.AddRaskPwa(new WebAppManifest
{
    Name = "Rask Server Showcase",
    ShortName = "Rask",
    Description = "The Rask component framework showcase, server-rendered with live updates over a WebSocket.",
    ThemeColor = "#512BD4",
    BackgroundColor = "#faf9fe",
    Display = DisplayMode.Standalone,
    Icons = [new ManifestIcon("icon.svg", "any", "image/svg+xml", "any maskable")],
    Categories = ["developer", "productivity"]
});
// Server-side Web Push backend (Rask.WebPush) for the Server PWA demo's subscribe→send loop.
builder.Services.AddPushDemo(builder.Configuration);
// The Server-only PWA showcase page contributes its sidebar entry to the shared ShowcaseLayout.
builder.Services.AddSingleton(new ShowcaseNavEntry("/server-pwa", "Server PWA", "bi-phone", "PWA"));
// Live-session capacity health check (Healthy / Degraded ≥80% / Unhealthy at cap), surfaced at
// /health below. Pairs with the OpenTelemetry-ready "Rask.Server" meter + activity source — see
// docs/observability.md.
builder.Services.AddHealthChecks().AddRaskLiveSessions();
// The HTTP demo's HttpClient calls back into this server for its own static
// data/posts-1.json. Resolve the bound origin lazily from IServerAddressesFeature
// (populated once the server is listening); fall back to localhost for the
// in-memory TestServer, which exposes no real address.
builder.Services.AddExampleServices(sp =>
{
    var addresses = sp.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()?.Addresses;
    var origin = addresses?.FirstOrDefault() ?? "http://localhost";
    return new Uri(origin.TrimEnd('/') + "/");
});

var app = builder.Build();

// Serve build-time static web assets — wwwroot/** AND package _content/* (e.g. Rask.Bootstrap's
// bundled CSS) — via MapStaticAssets, the .NET 9/10 static-asset pipeline: fingerprinting, build-time
// brotli/gzip, and immutable caching. The SDK auto-loads the static-web-assets manifest in Development;
// a published app carries the manifest plus the physical assets, so no extra wiring is needed there.
// (The E2E runs this host *published* so the slow-network journey exercises that production serving.)
//
// Dev note: `_content/Rask.Bootstrap/*` serves correctly for **package** consumers and for a **published**
// build, but a plain `dotnet run` of THIS in-repo showcase (which references Rask.Bootstrap by *project*)
// 500s on those assets — MapStaticAssets serves the compressed (.gz) variant and the in-repo project-ref
// build doesn't stage it (Rask.Bootstrap's package ships the SWA endpoints props, but its in-repo
// hand-written build/Rask.Bootstrap.props Exists()-skips it). A local-run-only quirk, no user/CI impact:
// `dotnet publish` (or run a package-based app) if you need the styled showcase locally.
app.MapStaticAssets();
app.UseRouting();

// Live-session capacity probe (see docs/observability.md). Returns 200 Healthy / Degraded and
// 503 Unhealthy once at the MaxSessions cap, so an orchestrator can shed load before refusals.
app.MapHealthChecks("/health");

// Test-only diagnostic endpoint: exposes the server's session count and GC heap
// so E2E memory/session-lifecycle tests can assert bounded growth. Lives in the
// example app, not the framework — adding a public diagnostics endpoint to
// Rask.Server itself would be a security-relevant decision the framework leaves
// to the host.
app.MapGet("/_diag", (LiveSessionStore store) =>
{
    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();
    return Results.Json(new
    {
        sessions = store.Count,
        gcMemoryBytes = GC.GetTotalMemory(true),
        gen0 = GC.CollectionCount(0),
        gen1 = GC.CollectionCount(1),
        gen2 = GC.CollectionCount(2)
    });
});

// To run two Rask servers side-by-side on one origin (behind a reverse proxy),
// pass a per-app prefix: app.UseRask<App>(pathBase: "/appA"). Every framework
// endpoint (WS, runtime script, scoped-asset endpoint, upload/download/auth)
// and every emitted URL (head asset links, history pushState) is scoped under
// the prefix. The client-side rask.js auto-strips/prepends the prefix via
// <base href>, so user-space route handlers stay unprefixed.
// Web Push backend endpoints (must precede the UseRask catch-all): GET /_push/key,
// POST /_push/subscribe, /_push/unsubscribe, /_push/send.
app.MapPushDemo();

app.UseRask<App>();

app.Run();
