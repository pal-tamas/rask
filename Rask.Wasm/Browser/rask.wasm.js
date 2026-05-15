// Rask WASM client runtime — ES module.
// .NET imports this via JSHost.ImportAsync("rask", "./rask.wasm.js") and calls the
// exported functions through [JSImport(name, "rask")] declarations.

let dotnetExports = null;
let root = null;
let lastCssHash = null;
let basePath = null;

// Read once from <base href> (or the page URL if no <base> is set) so the
// runtime can host under a sub-path like /Rask/ on GitHub Pages without the
// .NET side ever seeing the prefix. Resolves to the directory portion so a
// page URL like /index.html yields "/" (not "/index.html/").
function getBasePath() {
    if (basePath !== null) return basePath;
    const p = new URL(document.baseURI).pathname;
    const last = p.lastIndexOf("/");
    basePath = last < 0 ? "/" : p.slice(0, last + 1);
    return basePath;
}

function stripBase(pathname) {
    const b = getBasePath();
    if (b === "/" || !pathname) return pathname;
    if (pathname === b.slice(0, -1) || pathname === b) return "/";
    return pathname.startsWith(b) ? "/" + pathname.slice(b.length) : pathname;
}

function prependBase(url) {
    const b = getBasePath();
    if (b === "/" || typeof url !== "string" || !url.startsWith("/") || url.startsWith(b)) return url;
    return b + url.slice(1);
}

// Called from main.js once `getAssemblyExports` is available so the JS event
// handlers below can dispatch into .NET via the JSExport surface.
export function setExports(exports) {
    dotnetExports = exports;
    root = document.querySelector("[data-rask-root]") || document.body;
    const ok = !!(exports && exports.Rask && exports.Rask.Wasm
        && exports.Rask.Wasm.JSInterop && typeof exports.Rask.Wasm.JSInterop.Dispatch === "function");
    console.log("[Rask] setExports — Dispatch reachable:", ok, "root:", root && root.tagName);
}

// Called by .NET (via [JSImport]) for both the initial paint and subsequent
// background re-renders. `historyJson` is null for normal renders.
export function applyRender(html, cssHash, cssText, historyJson) {
    let history = null;
    if (typeof historyJson === "string" && historyJson.length > 0) {
        try {
            history = JSON.parse(historyJson);
        } catch (_) {
        }
    }
    handle({html, cssHash, cssText, history});
}

export function getLocation() {
    return stripBase(location.pathname) + location.search;
}

export function getBaseAddress() {
    return new URL(document.baseURI).href;
}

export function pushHistory(url, replace) {
    const target = prependBase(url);
    if (replace) window.history.replaceState({rask: true}, "", target);
    else window.history.pushState({rask: true}, "", target);
}

function inRoot(el) {
    return root && root.contains(el);
}

function applyScopedCss(hash, cssText) {
    if (hash === lastCssHash && cssText == null) return;
    lastCssHash = hash;
    let style = document.getElementById("rask-scoped");
    if (!style) {
        style = document.createElement("style");
        style.id = "rask-scoped";
        // Marker so morph() leaves this element alone — it's owned by JS, not
        // by the .NET render tree, and would otherwise get trimmed off when
        // the head's last child slot doesn't match between renders.
        style.setAttribute("data-rask-managed", "");
        document.head.appendChild(style);
    }
    if (typeof cssText === "string") style.textContent = cssText;
}

function applyHistory(history) {
    if (!history || typeof history.url !== "string") return;
    const target = prependBase(history.url);
    if (history.action === "replace")
        window.history.replaceState({rask: true}, "", target);
    else
        window.history.pushState({rask: true}, "", target);
}

function handle(reply) {
    if (!reply || typeof reply !== "object") return;
    let freshHtml = null;
    if (typeof reply.html === "string" && reply.html.length > 0) {
        const doc = new DOMParser().parseFromString(reply.html, "text/html");
        // Morph the whole <html> element so head changes (title, stylesheet links,
        // scoped-css link) propagate too — the App component owns the full page,
        // not just <body>. The bootstrap <script src="main.js"> in the original
        // index.html may get removed by morph if the App's body doesn't include
        // an equivalent; that's harmless because the module is already running.
        freshHtml = doc.documentElement;
    }
    const applyDom = () => {
        if (freshHtml) {
            morph(document.documentElement, freshHtml);
            root = document.querySelector("[data-rask-root]") || document.body;
        }
        applyHistory(reply.history);
        if (typeof window.raskAfterMorph === "function") window.raskAfterMorph();
    };
    // Animate navigations (renders carrying a history block) with the View
    // Transitions API when the browser supports it. State-only re-renders skip
    // the wrap so event-handler latency stays tight.
    if (reply.history && typeof document.startViewTransition === "function") {
        document.startViewTransition(applyDom);
    } else {
        applyDom();
    }
    if (typeof reply.cssHash === "string" || reply.cssHash === null)
        applyScopedCss(reply.cssHash, reply.cssText);
}

async function send(payload) {
    console.log("[Rask] send", payload);
    if (!dotnetExports) {
        console.warn("[Rask] send: dotnetExports not set");
        return;
    }
    if (!dotnetExports.Rask || !dotnetExports.Rask.Wasm || !dotnetExports.Rask.Wasm.JSInterop) {
        console.error("[Rask] send: Dispatch path missing on exports", dotnetExports);
        return;
    }
    try {
        const reply = await dotnetExports.Rask.Wasm.JSInterop.Dispatch(JSON.stringify(payload));
        console.log("[Rask] send reply bytes:", reply ? reply.length : 0);
        if (typeof reply === "string" && reply.length > 0) {
            try {
                handle(JSON.parse(reply));
            } catch (e) {
                console.error("Rask: malformed dispatch reply", e, reply);
            }
        }
    } catch (e) {
        console.error("Rask: dispatch failed", e);
    }
}

function morph(from, to) {
    if (from.nodeType !== to.nodeType || from.nodeName !== to.nodeName) {
        from.parentNode.replaceChild(to, from);
        return;
    }
    if (from.nodeType === 3 || from.nodeType === 8) {
        if (from.nodeValue !== to.nodeValue) from.nodeValue = to.nodeValue;
        return;
    }
    const fa = from.attributes, ta = to.attributes;
    for (let i = fa.length - 1; i >= 0; i--) {
        const name = fa[i].name;
        if (!to.hasAttribute(name)) from.removeAttribute(name);
    }
    for (let j = 0; j < ta.length; j++) {
        const a = ta[j];
        if (from.getAttribute(a.name) !== a.value) from.setAttribute(a.name, a.value);
    }
    const tag = from.tagName;
    if (tag === "INPUT" || tag === "TEXTAREA") {
        // Only inputs with data-rask-on-input stream keystrokes — those need the
        // focus guard so a lagging re-render doesn't clobber mid-typed characters.
        // Change-only inputs (date / number / time / datetime-local / checkbox /
        // radio) commit at change time; the server's rendered value is canonical
        // and must win, otherwise Chromium leaves a focused date input's dirty
        // value flag stale and the first picker change appears to be dropped.
        const streaming = from.hasAttribute("data-rask-on-input") || to.hasAttribute("data-rask-on-input");
        if (!streaming || document.activeElement !== from) {
            let newVal = to.getAttribute("value");
            if (newVal === null && to.tagName === "TEXTAREA") newVal = to.textContent;
            if (newVal === null) newVal = "";
            if (from.value !== newVal) from.value = newVal;
            const checked = to.hasAttribute("checked");
            if (from.checked !== checked) from.checked = checked;
        }
    }
    // Skip JS-owned elements (marked data-rask-managed) — they're not part of the
    // .NET render tree, so pairing them against the incoming children would either
    // trim them off or replace them with something unrelated.
    const fc = [], tc = [];
    for (let n = from.firstChild; n; n = n.nextSibling) {
        if (n.nodeType === 1 && n.hasAttribute("data-rask-managed")) continue;
        fc.push(n);
    }
    for (let m = to.firstChild; m; m = m.nextSibling) tc.push(m);
    const max = Math.max(fc.length, tc.length);
    for (let k = 0; k < max; k++) {
        const src = fc[k], dst = tc[k];
        if (!src) from.appendChild(dst);
        else if (!dst) from.removeChild(src);
        else if (src.nodeType !== dst.nodeType || src.nodeName !== dst.nodeName) from.replaceChild(dst, src);
        else morph(src, dst);
    }
}

document.addEventListener("click", (e) => {
    if (e.defaultPrevented) return;
    if (e.button !== 0 || e.metaKey || e.ctrlKey || e.shiftKey || e.altKey) return;
    const a = e.target.closest("a[data-rask-nav]");
    if (!a) return;
    console.log("[Rask] navlink click", a.getAttribute("href"));
    if (a.getAttribute("target") === "_blank") return;
    const href = a.getAttribute("href");
    if (!href) return;
    let url;
    try {
        url = new URL(href, location.href);
    } catch (_) {
        return;
    }
    if (url.origin !== location.origin) return;
    e.preventDefault();
    send({type: "navigate", path: stripBase(url.pathname), query: url.search});
});

window.addEventListener("popstate", () => {
    send({type: "navigate", path: stripBase(location.pathname), query: location.search, replace: true});
});

document.addEventListener("click", (e) => {
    const t = e.target.closest("[data-rask-on-click]");
    if (!t || !inRoot(t)) return;
    e.preventDefault();
    send({id: t.getAttribute("data-rask-on-click"), type: "click",
          shiftKey: e.shiftKey, ctrlKey: e.ctrlKey, altKey: e.altKey, metaKey: e.metaKey});
});

document.addEventListener("input", (e) => {
    const t = e.target.closest("[data-rask-on-input]");
    if (!t || !inRoot(t)) return;
    send({id: t.getAttribute("data-rask-on-input"), type: "input", value: t.value});
});

document.addEventListener("change", (e) => {
    const t = e.target.closest("[data-rask-on-change]");
    if (!t || !inRoot(t)) return;
    send({id: t.getAttribute("data-rask-on-change"), type: "change", value: t.value});
});

// scroll events don't bubble — listen in capture phase at the document level so we
// observe scroll on any descendant with [data-rask-on-scroll]. Coalesce bursts via
// rAF: one outgoing message per frame per element, even if scroll fires 5–10x.
const scrollPending = new Set();
let scrollRaf = 0;
function flushScroll() {
    scrollRaf = 0;
    scrollPending.forEach((el) => {
        if (!el.isConnected) return;
        const id = el.getAttribute("data-rask-on-scroll");
        if (!id) return;
        send({
            id,
            type: "scroll",
            scrollTop: el.scrollTop | 0,
            clientHeight: el.clientHeight | 0,
            scrollHeight: el.scrollHeight | 0
        });
    });
    scrollPending.clear();
}
document.addEventListener("scroll", (e) => {
    const t = e.target;
    if (!t || t.nodeType !== 1) return;
    if (!t.hasAttribute || !t.hasAttribute("data-rask-on-scroll")) return;
    if (!inRoot(t)) return;
    scrollPending.add(t);
    if (!scrollRaf) scrollRaf = requestAnimationFrame(flushScroll);
}, true);

document.addEventListener("submit", (e) => {
    const t = e.target.closest("[data-rask-on-submit]");
    if (!t || !inRoot(t)) return;
    e.preventDefault();
    const fd = new FormData(t);
    const obj = {};
    fd.forEach((v, k) => {
        obj[k] = String(v);
    });
    send({id: t.getAttribute("data-rask-on-submit"), type: "submit", form: obj});
});
