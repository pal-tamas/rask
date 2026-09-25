using Company.RaskServer.Client;
using Rask.Wasm;
// rask:if cqrs
using Rask.Cqrs.Client;
using Rask.Query;
// rask:end

// The browser app. It runs in WebAssembly and renders every page itself; the server's Program.cs is the
// other half — it answers this app's messages and serves the files this folder builds into.
//
// Client/ compiles into the browser only, Shared/ into both, everything else into the server only. So a
// message record belongs in Shared/, and its handler outside Client/ — where a connection string or a
// pricing rule can never end up in a download anybody can read.
var host = WasmHostBuilder.CreateDefault();
// rask:if cqrs

// Every message this app dispatches travels to the server, over the same IDispatcher call a server page
// makes in-process. A client is a PURE client: a handler compiled into the browser is bypassed, and
// [LocalOnly] is the only way to keep a message here.
host.Services.AddRaskCqrsClient();

// The same QueryClient a server page uses — Query, Command and Subscribe — over that dispatcher. A subscription
// opens on the server, so a page here hears what any visitor's command published.
host.Services.AddRaskQuery();
// rask:end
// rask:if pwa

// Installable PWA: the framework injects <link rel="manifest"> + <meta name="theme-color"> at boot, and
// wwwroot/index.html registers the service worker.
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
