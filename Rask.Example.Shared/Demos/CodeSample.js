// Per-instance Highlight.js bootstrap for the showcase's <CodeSample /> component.
// The framework's scoped-JS pipeline calls rendered(el, firstRender) against the
// outermost element of every rendered CodeSample (data-rask-mount="r-..." stamped
// by HtmlSerializer) when CodeSample.cs's OnRendered calls InvokeScopedJs. The hljs
// <link> + <script> dependencies are declared by CodeSample.Head and placed into
// <head> via the framework's auto-emit; by the time rendered runs the highlighter
// is either ready (synchronous script in head) or about to be — we check
// window.hljs and fall back to the script's load event if not yet ready.
// firstRender is unused here (hljs.highlightElement is idempotent so re-running on
// subsequent renders is harmless) but the parameter is plumbed through from the
// C#-side OnRendered(bool firstRender) lifecycle hook for cases that want to
// differentiate "first paint" from "subsequent renders".
export function rendered(el, firstRender) {
    const code = el.querySelector('code[class*="language-"]');
    if (!code) return;
    if (window.hljs) {
        delete code.dataset.highlighted;
        window.hljs.highlightElement(code);
        return;
    }
    // hljs is mid-flight: queue against its load event. The script is whatever
    // CodeSample.Head emitted, located by its CDN href.
    const script = document.querySelector('script[src*="highlight.min.js"]');
    if (!script) return;
    script.addEventListener('load', () => {
        if (!window.hljs) return;
        delete code.dataset.highlighted;
        window.hljs.highlightElement(code);
    }, { once: true });
}
