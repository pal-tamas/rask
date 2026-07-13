// Scoped JS for PlaygroundView. Three jobs, all browser-side:
//   * mountEditor(host, code) — create the Monaco code editor inside the host element (falls back to a
//     plain <textarea> if Monaco can't load, so the playground still works offline / if the CDN is blocked).
//   * editorValue(host) / setMarkers(host, json) — read the code, and paint compiler/analyzer diagnostics
//     (RASK0## + CS####) as inline squiggles.
//   * frameworkAssemblyUrls() — ask the live .NET runtime which assemblies it booted from, so the
//     in-browser compiler can download them and hand them to Roslyn as metadata references.

// Monaco is vendored under wwwroot/lib/monaco/vs (self-contained — no CDN, works offline and under the
// GitHub Pages sub-path). Resolve the base against <base href> so it's correct at both the origin root and
// /rask/playground/. Only the editor + base runtime + the C# grammar are vendored, not the heavy
// TypeScript/JSON/etc. language services.
const MONACO_BASE = new URL("lib/monaco/vs", document.baseURI).href;

let monacoApi = null;
let editor = null;
let loadPromise = null;

function loadMonaco() {
    if (monacoApi) return Promise.resolve(monacoApi);
    if (loadPromise) return loadPromise;

    loadPromise = new Promise((resolve, reject) => {
        const loader = document.createElement("script");
        loader.src = `${MONACO_BASE}/loader.js`;
        loader.onload = () => {
            const req = globalThis.require;
            req.config({ paths: { vs: MONACO_BASE } });
            // The editor's helper worker loads via a blob that importScripts the vendored worker and points
            // its baseUrl back at our vs/ folder — same pattern regardless of same/cross origin.
            self.MonacoEnvironment = {
                getWorkerUrl() {
                    const shim = `self.MonacoEnvironment={baseUrl:'${MONACO_BASE}/'};` +
                        `importScripts('${MONACO_BASE}/base/worker/workerMain.js');`;
                    return URL.createObjectURL(new Blob([shim], { type: "text/javascript" }));
                }
            };
            req(["vs/editor/editor.main"], () => {
                monacoApi = globalThis.monaco;
                resolve(monacoApi);
            }, reject);
        };
        loader.onerror = reject;
        document.head.appendChild(loader);
    });
    return loadPromise;
}

// Monaco injects its theme colours as a <style class="monaco-colors"> (and its CSS as a <link>) into
// <head>. Rask's live-diff morph reconciles <head> on every re-render and removes any child that isn't
// marked data-rask-managed — the same marker the framework uses for its own scoped-asset head tags — so
// without this the editor loses all colour the first time the app re-renders (e.g. after Run). Stamp
// Monaco's head nodes as managed, and keep watching <head> so any it adds later is protected too.
let headGuardInstalled = false;

function isMonacoHeadNode(n) {
    if (!n || n.nodeType !== 1) return false;
    const cls = typeof n.className === "string" ? n.className : "";
    if (cls.indexOf("monaco") !== -1) return true;
    if (n.tagName === "STYLE" && n.textContent && n.textContent.indexOf(".mtk") !== -1) return true;
    if (n.tagName === "LINK" && n.href && n.href.indexOf("/monaco/") !== -1) return true;
    return false;
}

function markManaged(n) {
    if (isMonacoHeadNode(n) && !n.hasAttribute("data-rask-managed")) {
        n.setAttribute("data-rask-managed", "");
    }
}

function protectMonacoHeadNodes() {
    // Idempotent sweep of anything already in <head>.
    document.head.querySelectorAll("style, link").forEach(markManaged);
    // Install the ongoing guard exactly once.
    if (headGuardInstalled) return;
    headGuardInstalled = true;
    new MutationObserver((records) => {
        for (const r of records) for (const n of r.addedNodes) markManaged(n);
    }).observe(document.head, { childList: true });
}

export async function mountEditor(host, code) {
    if (!host || editor || host.__fallback) return;
    try {
        const monaco = await loadMonaco();
        // Start protecting head nodes before create so the color <style> Monaco injects during create is
        // caught by the observer and marked managed from the outset.
        protectMonacoHeadNodes();
        editor = monaco.editor.create(host, {
            value: code,
            language: "csharp",
            theme: "vs-dark",
            automaticLayout: true,
            minimap: { enabled: false },
            scrollBeyondLastLine: false,
            fontSize: 13,
            tabSize: 4,
            renderLineHighlight: "line",
            fixedOverflowWidgets: true
        });
        // Re-sweep after create in case any node landed synchronously before the observer registered.
        protectMonacoHeadNodes();
    } catch {
        // Monaco unavailable — degrade to a textarea so the playground still compiles and runs code.
        const ta = document.createElement("textarea");
        ta.className = "pg-fallback";
        ta.spellcheck = false;
        ta.value = code;
        host.appendChild(ta);
        host.__fallback = ta;
    }
}

export function editorValue(host) {
    if (editor) return editor.getValue();
    if (host && host.__fallback) return host.__fallback.value;
    const ta = host && host.querySelector ? host.querySelector("textarea") : null;
    return ta ? ta.value : "";
}

export function setMarkers(host, diagnosticsJson) {
    if (!editor || !monacoApi) return;
    let diagnostics;
    try {
        diagnostics = JSON.parse(diagnosticsJson);
    } catch {
        return;
    }

    const severity = (s) =>
        s === "Error" ? monacoApi.MarkerSeverity.Error
            : s === "Warning" ? monacoApi.MarkerSeverity.Warning
                : monacoApi.MarkerSeverity.Info;

    const markers = diagnostics.map((d) => ({
        severity: severity(d.severity),
        message: `${d.id}: ${d.message}`,
        startLineNumber: d.startLine,
        startColumn: d.startColumn,
        endLineNumber: d.endLine,
        endColumn: d.endColumn
    }));

    monacoApi.editor.setModelMarkers(editor.getModel(), "rask", markers);
}

// The playground ships its managed assemblies as plain PE under _framework/ (WasmEnableWebcil=false), and
// the runtime's own boot config is the authoritative list of exactly which ones (fingerprinted names).
// Read it via getDotnetRuntime(0).getConfig(); be liberal about the resource-group shape across runtime
// versions (arrays of {name} or name→hash maps), and filter to managed assemblies (drop the native
// runtime, ICU and pdbs).
export function frameworkAssemblyUrls() {
    const runtime = globalThis.getDotnetRuntime ? globalThis.getDotnetRuntime(0) : null;
    const config = runtime && runtime.getConfig ? runtime.getConfig() : null;
    const resources = (config && config.resources) || {};

    const names = new Set();
    const collect = (group) => {
        if (!group) return;
        if (Array.isArray(group)) {
            for (const item of group) names.add(typeof item === "string" ? item : item && item.name);
        } else if (typeof group === "object") {
            for (const key of Object.keys(group)) names.add(key);
        }
    };

    // Scan every resource group rather than a fixed set of keys — the group names have shifted across
    // .NET versions (assembly / coreAssembly / fingerprinting / lazyAssembly …). The isManagedAssembly
    // filter below drops everything that isn't a managed assembly (the native runtime, ICU, JS, pdbs,
    // satellite culture keys), so over-collecting here is harmless and future-proof.
    for (const key of Object.keys(resources)) {
        collect(resources[key]);
    }

    const isManagedAssembly = (n) =>
        typeof n === "string" &&
        /\.(wasm|dll)$/i.test(n) &&
        !/^dotnet(\.|$)/i.test(n) &&
        !/icudt/i.test(n) &&
        !/\.pdb$/i.test(n);

    const base = document.baseURI;
    const urls = [];
    const seen = new Set();
    for (const n of names) {
        if (!isManagedAssembly(n) || seen.has(n)) continue;
        seen.add(n);
        urls.push(new URL("_framework/" + n, base).href);
    }
    return urls;
}
