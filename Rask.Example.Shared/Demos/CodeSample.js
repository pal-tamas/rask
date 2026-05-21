// Highlight every `.sample-card code[class*="language-"]` on the page via
// highlight.js. The framework concatenates this file into the scoped-JS
// bundle (auto-globbed by Rask.Core.targets) and wraps it as
// `window.Rask.CodeSample = { rendered, … }`; user C# calls into it via
// `js.InvokeVoidAsync("Rask.CodeSample.rendered", firstRender)` from
// CodeSample.OnRenderedAsync.
//
// The hljs <link> + <script> dependencies come in through CodeSample.Head —
// by the time `rendered` first runs, hljs is either ready (sync <script> in
// head) or about to be. The fallback subscribes to the script's load event.
// firstRender is unused (hljs.highlightElement is idempotent) but the
// parameter is preserved for forward-compat.
export function rendered(firstRender) {
    const codes = document.querySelectorAll('.sample-card code[class*="language-"]');
    if (codes.length === 0) return;
    const apply = () => {
        codes.forEach(code => {
            delete code.dataset.highlighted;
            window.hljs.highlightElement(code);
        });
    };
    if (window.hljs) {
        apply();
        return;
    }
    const script = document.querySelector('script[src*="highlight.min.js"]');
    if (!script) return;
    script.addEventListener('load', () => {
        if (window.hljs) apply();
    }, {once: true});
}
