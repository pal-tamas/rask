using Rask.Example.Shared;
using Rask.Wasm;

// Framework default is LiveDiffMode.Auto — counter increments and similar
// in-place state changes go over the wire as a handful of bytes instead of the
// whole rendered body. Open the network panel in the browser to see it.
//
// PathBase is auto-detected at boot from <base href> (rask.wasm.js's getBasePath
// export, read via JSImport in WasmHostBuilder). Publish with
// /p:RaskPathBase=/myapp to rewrite the bundled index.html's <base href> for
// sub-path deploys (GH Pages, plain static hosts). Override explicitly with
// WasmHostBuilder.CreateDefault(o => o.PathBase = "/myapp") when needed.
var host = WasmHostBuilder.CreateDefault();
// The HTTP demo's HttpClient fetches data/posts-1.json from the AppBundle served at
// the page origin. WasmHostBuilder.BaseAddress carries any sub-path (e.g. the GitHub
// Pages /Rask/ prefix); read it lazily inside the factory so it resolves after the
// JS module import.
host.Services.AddExampleServices(_ => new Uri(WasmHostBuilder.BaseAddress));
await host.RunAsync<App>();
