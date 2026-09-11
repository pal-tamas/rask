using Company.RaskServer.Features.Shared;
using Rask.Wasm;
// rask:if pwa
using Rask.Core.Browser;
// rask:end
// PathBase is auto-detected at boot from <base href>. For sub-path deploys
// (e.g. GH Pages at https://<user>.github.io/<repo>/), publish with
// /p:RaskPathBase=/<repo> — the framework rewrites the published
// index.html's <base href> so the runtime picks up the prefix on first paint
// and head-emitted asset URLs are scoped under /<repo>/_rask/a/{hash}.{ext}. Override
// explicitly via WasmHostBuilder.CreateDefault(o => o.PathBase = "/myapp")
// if you need to set it from .NET code.
var host = WasmHostBuilder.CreateDefault();
// rask:if pwa
// Installable PWA: the framework injects <link rel="manifest"> + <meta name="theme-color"> at boot.
host.UsePwa(new WebAppManifest
{
    Name = "Rask App",
    ShortName = "Rask App",
    ThemeColor = "#512BD4",
    BackgroundColor = "#faf9fe",
    Display = DisplayMode.Standalone,
    Icons = [new ManifestIcon("icon.svg", "any", "image/svg+xml", "any maskable")]
});
// rask:end

await host.RunAsync<App>();
