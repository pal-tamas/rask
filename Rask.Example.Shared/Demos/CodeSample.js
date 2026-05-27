// Highlight every `.sample-card code[class*="language-"]` on the page via
// highlight.js. The framework concatenates this file into the scoped-JS
// bundle (auto-globbed by Rask.Core.targets) and wraps it as
// `window.Rask.CodeSample = { rendered, … }`; user C# calls into it via
// `js.InvokeVoidAsync("Rask.CodeSample.rendered", firstRender)` from
// CodeSample.OnRenderedAsync.
//
// The hljs <link> + <script> dependencies come in through CodeSample.Head.
// The Rask runtime gates every Rask.* JS invoke until each Head-declared
// external <script src> and <link rel=stylesheet> fires a terminal event
// — load, error, OR a 5-second safety timeout. Load is the happy path;
// error/timeout still drain so the page doesn't hang. That means
// `window.hljs` is observable here but NOT guaranteed defined: a refresh
// that hits a stale-cache 'error' on highlight.min.js, an extension
// blocking the CDN, an integrity / CORS mismatch, or a network blip
// during cache revalidation all surface as `typeof window.hljs ===
// "undefined"` at invoke time. The guard below degrades gracefully
// (un-highlighted code blocks render fine) instead of throwing a
// TypeError that the framework would marshal into a "Something went
// wrong" RootErrorBoundary fallback.
//
// The dataset.highlighted guard makes the function idempotent across the
// framework's OnRenderedAsync replays so re-firing it on a cached instance
// post-morph is a cheap no-op instead of tearing down and rebuilding the
// spans.
export function rendered(firstRender) {
    if (typeof window.hljs === "undefined" || typeof window.hljs.highlightElement !== "function") {
        // hljs <script> didn't successfully define its global — almost
        // always a transient asset-load failure. Leave code blocks as
        // plain monospace text rather than crashing the page.
        return;
    }
    const codes = document.querySelectorAll('.sample-card code[class*="language-"]');
    if (codes.length === 0) return;
    codes.forEach(code => {
        if (code.dataset.highlighted) return;
        window.hljs.highlightElement(code);
    });
}
