using Rask.Example.Shared;
using Rask.Wasm.Hosting;

var builder = WebApplication.CreateBuilder(args);

// Opt into brotli + gzip response compression for the AppBundle. UseRask wires the
// response-compression middleware ahead of UseStaticFiles when this registration is
// present; without it the bundle still serves, just uncompressed.
builder.Services.AddRask();

var app = builder.Build();

// Generic UseRask<App> touches the App type, which forces the runtime to load the
// Rask.Example.Shared assembly and fire its [ModuleInitializer] attributes. Those
// initializers populate ScopedAssetRegistry with the same per-component hashes the
// in-browser WASM runtime computes — without this, every browser request to
// /_rask/a/{hash}.{ext} would 404 because the host's registry was empty.
// To host two WASM AppBundles side-by-side in one process, pass a per-app prefix:
// app.UseRask<App>(pathBase: "/appA"). The asset endpoint and static-file
// middleware both scope under the prefix. Pair with /p:RaskPathBase=/appA at
// publish time so the bundled index.html's <base href> matches.
//
// RASK_PATHBASE / RASK_BUNDLE_DIR env vars let the E2E sub-path fixture
// (Rask.Examples.E2E.Tests/SubPathWasmAppFixture) point the same example host
// at a re-published AppBundle under a prefix without a dedicated executable.
// In a real app you'd just hardcode the pathBase you want.
app.UseRask<App>(
    Environment.GetEnvironmentVariable("RASK_BUNDLE_DIR"),
    Environment.GetEnvironmentVariable("RASK_PATHBASE") ?? string.Empty);

app.Run();
