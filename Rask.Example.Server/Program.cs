using Rask.Example.Shared;
using Rask.Server;

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

app.UseRask<App>();

app.Run();
