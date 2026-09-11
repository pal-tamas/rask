using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Browser;
using Rask.Site;
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
// (PwaDemo, WakeLockDemo, …) live in this app assembly, not Rask.Site, so register it with
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
// What /docs/guides/{slug} expands to at publish. A parameterised route has no path without data, so
// the prerender pass skips it — and the guides are the site, so skipping it means ~80 documents ship to
// crawlers as an empty boot shell while the publish reports every page it knew about as written.
host.Services.AddSingleton<Rask.Core.Live.IPrerenderPaths, Rask.Site.Features.GuidePrerenderPaths>();

// These pages contribute their sidebar entries here rather than in ShowcaseLayout's own table.
//
// The paths are the GENERATED route URLs, not literals. They WERE literals — "/pwa", "/islands" — and
// the day the showcase moved from / to /docs every one of them became a dead sidebar link pointing at a
// URL with no route behind it. Nothing failed to build; the entries rendered, the links looked right,
// and clicking one landed on "No route is registered for /pwa".
//
// ShowcaseLayout's own link table already had this right, and says so in a comment: a renamed or removed
// [Route] is a compile error there rather than a dead link. These entries simply bypassed it.
// ONE row for all thirteen PWA / device-capability demos. They were thirteen rows, each a page whose
// whole body was a heading, a paragraph and one CodeSample — a list a reader had to read in full to
// find that twelve of them teach the same idea. PwaPage still answers all thirteen old URLs (repeated
// [Route]), so nothing that was linked or bookmarked 404s.
host.Services.AddSingleton(new ShowcaseNavEntry(Rask.Site.Features.Routes.PwaPage(), "PWA & device APIs", UiIconName.Phone, "PWA"));
// The islands showcase: the same .vue/.tsx/.svelte the Server host builds, mounted client-side.
host.Services.AddSingleton(new ShowcaseNavEntry(Rask.Site.Features.Islands.Routes.IslandsPage(), "Islands", UiIconName.Overview, "Islands"));
// The UI kit, one page per daisyUI category. The guide at /guides/ui-kit is the prose; these are the
// components themselves, live, which is the half a paragraph cannot do.
host.Services.AddSingleton(new ShowcaseNavEntry(Rask.Site.Features.UiKit.Routes.UiKitActionsPage(), "Actions", UiIconName.Check, "UI kit"));
host.Services.AddSingleton(new ShowcaseNavEntry(Rask.Site.Features.UiKit.Routes.UiKitDataDisplayPage(), "Data display", UiIconName.Overview, "UI kit"));
host.Services.AddSingleton(new ShowcaseNavEntry(Rask.Site.Features.UiKit.Routes.UiKitDataGridPage(), "Data grid", UiIconName.Stack, "UI kit"));
host.Services.AddSingleton(new ShowcaseNavEntry(Rask.Site.Features.UiKit.Routes.UiKitNavigationPage(), "Navigation", UiIconName.Menu, "UI kit"));
host.Services.AddSingleton(new ShowcaseNavEntry(Rask.Site.Features.UiKit.Routes.UiKitFeedbackPage(), "Feedback", UiIconName.Warning, "UI kit"));
host.Services.AddSingleton(new ShowcaseNavEntry(Rask.Site.Features.UiKit.Routes.UiKitDataInputPage(), "Data input", UiIconName.Pencil, "UI kit"));
host.Services.AddSingleton(new ShowcaseNavEntry(Rask.Site.Features.UiKit.Routes.UiKitLayoutPage(), "Layout & mockups", UiIconName.Desktop, "UI kit"));
// The docs' plain-text face for AI assistants: /llms.txt, /llms-full.txt and a .md twin beside every guide.
// Only the prerender publish sets this variable, and it is the one run with a publish directory to write
// into — a browser boot never takes this branch.
if (Environment.GetEnvironmentVariable(WasmPrerender.OutputVariable) is { Length: > 0 } publishRoot)
{
    Rask.Site.Features.LlmsText.WriteAll(publishRoot);
}

await host.RunAsync<App>();
