using Rask.Example.Crdt;
using Rask.Example.Crdt.Devices;
using Rask.Server;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRask();

// Three devices, built once at startup: each gets its own SQLite database and its own replica identity,
// and they share a bucket and nothing else. Normally these would be three phones; here they are three
// files in one process, which is the only difference that matters.
//
// cr-sqlite's native binary is not redistributed here, so RASK_CRSQLITE_PATH must point at the one for
// this platform. Without it the page explains what to download instead of failing at the first query.
var family = await FamilyDevices.CreateAsync(builder.Configuration);
builder.Services.AddSingleton(family);

builder.Services.Configure<HostOptions>(options =>
{
    options.ShutdownTimeout = TimeSpan.FromSeconds(15);
    options.ServicesStopConcurrently = true;
});

var app = builder.Build();

app.Lifetime.ApplicationStopped.Register(() => family.DisposeAsync().AsTask().GetAwaiter().GetResult());

app.MapStaticAssets();
app.UseRouting();
app.UseRask<App>();

app.Run();
