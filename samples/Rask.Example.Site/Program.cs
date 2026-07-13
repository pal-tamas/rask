using Rask.Example.Site;
using Rask.Wasm;

// PathBase is auto-detected at boot from <base href>. For the GitHub Pages sub-path deploy
// (https://<user>.github.io/<repo>/), CI publishes with /p:RaskPathBase=/<repo> — the framework
// rewrites the published index.html's <base href> so the runtime picks up the prefix on first paint.
var host = WasmHostBuilder.CreateDefault();

await host.RunAsync<App>();
