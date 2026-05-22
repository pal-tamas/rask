// Highlight every `.sample-card code[class*="language-"]` on the page via
// highlight.js. The framework concatenates this file into the scoped-JS
// bundle (auto-globbed by Rask.Core.targets) and wraps it as
// `window.Rask.CodeSample = { rendered, … }`; user C# calls into it via
// `js.InvokeVoidAsync("Rask.CodeSample.rendered", firstRender)` from
// CodeSample.OnRenderedAsync.
//
// The hljs <link> + <script> dependencies come in through CodeSample.Head.
// The Rask runtime gates every Rask.* JS invoke until all Head-declared
// external <script src> tags have fired their load event AND all
// <link rel=stylesheet> tags have loaded — so window.hljs is guaranteed to
// be defined and the hljs stylesheet applied before this function runs. No
// per-component load-event workaround is needed.
//
// The dataset.highlighted guard makes the function idempotent across the
// framework's OnRenderedAsync replays so re-firing it on a cached instance
// post-morph is a cheap no-op instead of tearing down and rebuilding the
// spans.
export function rendered(firstRender) {
    const codes = document.querySelectorAll('.sample-card code[class*="language-"]');
    if (codes.length === 0) return;
    codes.forEach(code => {
        if (code.dataset.highlighted) return;
        window.hljs.highlightElement(code);
    });
}
