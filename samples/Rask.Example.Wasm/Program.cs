using Microsoft.Extensions.DependencyInjection;
using Rask.Blazor;
using Rask.Core.Browser;
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
// CodeSample reads demo sources embedded as raksrc/{leaf} manifest resources. The WASM-only demos
// (PwaDemo, WakeLockDemo, …) live in this app assembly, not Rask.Example.Shared, so register it with
// EmbeddedSource — otherwise the lookup only sees the shared assembly and can't find them.
EmbeddedSource.RegisterAssembly(System.Reflection.Assembly.GetExecutingAssembly());
// The Blazor island page shows the hosted .razor beside the C# that hosts it, and that file lives in
// the referenced Razor Class Library rather than here.
EmbeddedSource.RegisterAssembly(typeof(Rask.Example.Razor.PriceTicker).Assembly);

var host = WasmHostBuilder.CreateDefault();
host.Services.AddRaskBlazor();
// The HTTP demo's HttpClient fetches data/posts-1.json from the AppBundle served at
// the page origin. WasmHostBuilder.BaseAddress carries any sub-path (e.g. the GitHub
// Pages /Rask/ prefix); read it lazily inside the factory so it resolves after the
// JS module import.
host.Services.AddExampleServices(_ => new Uri(WasmHostBuilder.BaseAddress));
// Typed PWA manifest — the framework injects <link rel="manifest"> + <meta name="theme-color"> at
// boot (a data: URL with sub-path-correct absolute URLs), so there's no manifest.webmanifest to ship.
host.UsePwa(new WebAppManifest
{
    Name = "Rask WASM Showcase",
    ShortName = "Rask",
    Description = "The Rask component framework showcase, running entirely in the browser as a WASM PWA.",
    ThemeColor = "#512BD4",
    BackgroundColor = "#faf9fe",
    Display = DisplayMode.Standalone,
    Icons = [new ManifestIcon("icon.svg", "any", "image/svg+xml", "any maskable")],
    Categories = ["developer", "productivity"],
    Shortcuts =
    [
        new ManifestShortcut("Browser APIs", "browser/clipboard", ShortName: "APIs",
            Description: "Jump straight to the Browser APIs showcase")
    ]
});
// WASM-only example pages — contribute their sidebar entries to the shared ShowcaseLayout. These APIs
// can't run on the Server transport, so they live in the WASM host rather than the shared showcase.
host.Services.AddSingleton(new ShowcaseNavEntry("/pwa", "PWA demo", UiIconName.Phone, "PWA"));
// The islands showcase: the same .vue/.tsx/.svelte the Server host builds, mounted client-side.
host.Services.AddSingleton(new ShowcaseNavEntry("/islands", "Islands", UiIconName.Overview, "Islands"));
host.Services.AddSingleton(new ShowcaseNavEntry("/blazor-island", "Blazor island", UiIconName.CodeBracket, "Islands"));
host.Services.AddSingleton(new ShowcaseNavEntry("/install", "Install prompt", UiIconName.Download, "PWA"));
host.Services.AddSingleton(new ShowcaseNavEntry("/wake-lock", "Wake lock", UiIconName.Desktop, "PWA"));
host.Services.AddSingleton(new ShowcaseNavEntry("/orientation", "Orientation", UiIconName.Phone, "PWA"));
host.Services.AddSingleton(new ShowcaseNavEntry("/fullscreen", "Fullscreen", UiIconName.Fullscreen, "PWA"));
host.Services.AddSingleton(new ShowcaseNavEntry("/picture-in-picture", "Picture-in-Picture", UiIconName.Desktop, "PWA"));
host.Services.AddSingleton(new ShowcaseNavEntry("/eyedropper", "EyeDropper", UiIconName.EyeDropper, "PWA"));
host.Services.AddSingleton(new ShowcaseNavEntry("/idle", "Idle detection", UiIconName.Clock, "PWA"));
host.Services.AddSingleton(new ShowcaseNavEntry("/media-devices", "Camera & mic", UiIconName.VideoCamera, "PWA"));
host.Services.AddSingleton(new ShowcaseNavEntry("/serial", "Serial port", UiIconName.Cube, "PWA"));
host.Services.AddSingleton(new ShowcaseNavEntry("/usb", "USB device", UiIconName.Cube, "PWA"));
host.Services.AddSingleton(new ShowcaseNavEntry("/hid", "HID device", UiIconName.Cube, "PWA"));
host.Services.AddSingleton(new ShowcaseNavEntry("/bluetooth", "Bluetooth", UiIconName.Signal, "PWA"));
await host.RunAsync<App>();
