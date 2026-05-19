// Per-instance Highlight.js bootstrap for the showcase's <CodeSample /> component.
// The framework's scoped-JS pipeline calls rendered(el) against the outermost element
// of every rendered CodeSample (data-rask-mount="r-..." stamped by HtmlSerializer).
// The hljs <link> + <script> dependencies are declared by CodeSample.Head and placed
// into <head> by the framework's RaskHeadAssets sentinel, so by the time rendered runs
// the highlighter is either ready (synchronous script in head) or about to be — we
// check window.hljs and fall back to the script's load event if not yet ready.
export function rendered(el) {
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
