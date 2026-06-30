using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Rask.Core.Live;
using Rask.Example.Shared;
using Rask.Server;
using Rask.Server.Diagnostics;

// The framework default since AddRask gained an options shape is
// LiveDiffMode.Auto, so `services.AddRask()` already ships the diff codec out of
// the box. The RASK_DIFF_MODE env var lets the Playwright suite (and curious
// developers) flip modes without recompiling — useful for diff-vs-morph A/B
// debugging.
if (Environment.GetEnvironmentVariable("RASK_DIFF_MODE") is { } diffModeName
    && Enum.TryParse<LiveDiffMode>(diffModeName, true, out var diffMode))
{
    LiveOptions.DiffMode = diffMode;
}

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRask();
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
app.UseRask<App>();

app.Run();
