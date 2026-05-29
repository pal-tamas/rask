using Rask.Core.Live;
using Rask.Example.Shared;
using Rask.Server;

// The framework default since AddRask gained an options shape is
// LiveDiffMode.Auto, so `services.AddRask()` already ships the diff codec out of
// the box. The RASK_DIFF_MODE env var lets the Playwright suite (and curious
// developers) flip modes without recompiling — useful for diff-vs-morph A/B
// debugging.
if (Environment.GetEnvironmentVariable("RASK_DIFF_MODE") is { } diffModeName
    && Enum.TryParse<LiveDiffMode>(diffModeName, ignoreCase: true, out var diffMode))
{
    LiveOptions.DiffMode = diffMode;
}

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRask();
builder.Services.AddExampleServices();

var app = builder.Build();

app.UseStaticFiles();

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
        gcMemoryBytes = GC.GetTotalMemory(forceFullCollection: true),
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
