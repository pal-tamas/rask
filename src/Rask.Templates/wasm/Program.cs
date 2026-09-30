using Company.RaskServer.Features.Shared;
using Rask.Wasm;

// Served under a sub-path (GitHub Pages)? Publish with -p:RaskPathBase=/<repo> — see docs/deployment.md.
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
