using Rask.Example.Site;
using Rask.Wasm;

// PathBase is auto-detected at boot from <base href>. This app is the landing page and is served
// from the origin root (https://rask.sh/), so CI publishes it with no RaskPathBase and the default
// <base href="/"> already resolves every asset. The nested app on the same site — the docs showcase
// at /docs/ — does pass /p:RaskPathBase, and the framework rewrites its published index.html's
// <base href> so the runtime picks the prefix up on first paint.
var host = WasmHostBuilder.CreateDefault();

await host.RunAsync<App>();
