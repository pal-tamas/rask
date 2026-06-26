using Microsoft.Extensions.DependencyInjection;
using Rask.Example.Shared;
using Rask.Wasm;
using Rask.Wasm.Browser;

// Framework default is LiveDiffMode.Auto — counter increments and similar
// in-place state changes go over the wire as a handful of bytes instead of the
// whole rendered body. Open the network panel in the browser to see it.
//
// PathBase is auto-detected at boot from <base href> (rask.wasm.js's getBasePath
// export, read via JSImport in WasmHostBuilder). Publish with
// /p:RaskPathBase=/myapp to rewrite the bundled index.html's <base href> for
// sub-path deploys (GH Pages, plain static hosts). Override explicitly with
// WasmHostBuilder.CreateDefault(o => o.PathBase = "/myapp") when needed.
// CodeSample reads demo sources embedded as raksrc/{leaf} manifest resources. The WASM-only demos
// (PwaDemo, WakeLockDemo, …) live in this app assembly, not Rask.Example.Shared, so register it with
// EmbeddedSource — otherwise the lookup only sees the shared assembly and can't find them.
EmbeddedSource.RegisterAssembly(System.Reflection.Assembly.GetExecutingAssembly());

var host = WasmHostBuilder.CreateDefault();
// The HTTP demo's HttpClient fetches data/posts-1.json from the AppBundle served at
// the page origin. WasmHostBuilder.BaseAddress carries any sub-path (e.g. the GitHub
// Pages /Rask/ prefix); read it lazily inside the factory so it resolves after the
// JS module import.
host.Services.AddExampleServices(_ => new Uri(WasmHostBuilder.BaseAddress));
// Typed PWA manifest — the framework injects <link rel="manifest"> + <meta name="theme-color"> at
// boot (a data: URL with sub-path-correct absolute URLs), so there's no manifest.webmanifest to ship.
host.UseManifest(new WebAppManifest
{
    Name = "Rask WASM Showcase",
    ShortName = "Rask",
    Description = "The Rask component framework showcase, running entirely in the browser as a WASM PWA.",
    ThemeColor = "#512BD4",
    BackgroundColor = "#faf9fe",
    Display = DisplayMode.Standalone,
    Icons = [new ManifestIcon("icon.svg", "any", "image/svg+xml", "any maskable")]
});
// WASM-only example pages — contribute their sidebar entries to the shared ShowcaseLayout. These APIs
// can't run on the Server transport, so they live in the WASM host rather than the shared showcase.
host.Services.AddSingleton(new ShowcaseNavEntry("/pwa", "PWA demo", "bi-phone", "PWA"));
host.Services.AddSingleton(new ShowcaseNavEntry("/wake-lock", "Wake lock", "bi-display", "PWA"));
host.Services.AddSingleton(new ShowcaseNavEntry("/orientation", "Orientation", "bi-phone-landscape", "PWA"));
host.Services.AddSingleton(new ShowcaseNavEntry("/fullscreen", "Fullscreen", "bi-fullscreen", "PWA"));
await host.RunAsync<App>();
