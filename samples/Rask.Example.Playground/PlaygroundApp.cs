using Rask.Bootstrap;
using Rask.Core.Live;

namespace Rask.Example.Playground;

// Root of the playground WASM app. Renders into the framework's <body>; the single-page UI,
// editor, compile orchestration and live preview all live in PlaygroundView. Public + non-sealed to match
// the host's ActivatorUtilities.CreateInstance + DAM contract.
public partial class PlaygroundApp : Component
{
    // Dark-first theme init — mirrors the showcase App.cs / index.html snippet: stamp data-theme +
    // data-bs-theme on <html> from the saved choice (shared across the site/docs/playground on this
    // origin) or the OS preference before paint, and re-apply the saved choice after the WASM
    // full-document morph strips the attributes off <html> on boot.
    private const string ThemeInitJs =
        "(function(){var d=document.documentElement;" +
        "function apply(t){d.setAttribute('data-theme',t);d.setAttribute('data-bs-theme',t);}" +
        "var saved=localStorage.getItem('rask-theme');" +
        "apply(saved||(matchMedia('(prefers-color-scheme: dark)').matches?'dark':'light'));" +
        "var prev=window.raskAfterMorph;" +
        "window.raskAfterMorph=function(){var s=localStorage.getItem('rask-theme');if(s)apply(s);" +
        "if(typeof prev==='function')prev();};})();";

    protected override Component? HeadAssets =>
    [
        Title["Rask Playground"],
        Meta.Charset("utf-8"),
        Meta.Name("viewport").Content("width=device-width, initial-scale=1"),
        Script[Raw.Value(ThemeInitJs)],
        Link.Rel("icon").Type("image/svg+xml").Href(LiveOptions.PathBase + "/icon.svg"),
        // Typed Bootstrap + the shared design tokens (the --bs-* bridge wins over Bootstrap; global.css
        // then overrides tokens). Same order as the showcase App.cs.
        BootstrapStyles,
        RaskTokens,
        Link.Rel("stylesheet").Href(LiveOptions.PathBase + "/global.css")
    ];

    protected override Component? Render() => PlaygroundView;
}
