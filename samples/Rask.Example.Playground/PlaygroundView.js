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

// Language-feature state (set up once, after the .NET side finishes loading the framework references).
let languageRegistered = false;
let diagnoseTimer = 0;
// The assembly that hosts PlaygroundLanguageInterop's [JSInvokable]s (window.DotNet dispatches by name).
const PLAYGROUND_ASSEMBLY = "Rask.Example.Playground";

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

export async function mountEditor(host, code) {
    if (!host || editor || host.__fallback) return;
    try {
        const monaco = await loadMonaco();
        // Monaco injects its theme colours as a <style> into <head>. Rask preserves foreign head nodes
        // automatically (its live-diff reconciler tags what a library injects), so the editor keeps its
        // colours across re-renders (e.g. after Run) with no extra guarding here.
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
    applyMarkers(diagnosticsJson);
}

// Replace the editor's whole buffer — used by the example gallery and Reset. editor.setValue fires the
// model's change event, so live diagnostics refresh for the new code with nothing else to trigger.
export function setEditorValue(host, code) {
    if (editor) {
        editor.setValue(code);
        editor.setScrollTop(0);
        return;
    }
    if (host && host.__fallback) {
        host.__fallback.value = code;
        return;
    }
    const ta = host && host.querySelector ? host.querySelector("textarea") : null;
    if (ta) ta.value = code;
}

// Turn the editor into an IDE, once the framework references have loaded on the .NET side. Wires three
// things, all feeding the static [JSInvokable]s in PlaygroundLanguageInterop via window.DotNet.invokeMethodAsync
// (the same JS→.NET dispatch the framework's own browser wrappers use):
//   (1) IntelliSense — a Roslyn-backed completion provider,
//   (2) as-you-type diagnostics — debounced on every edit, and
//   (3) Ctrl/Cmd + Enter to Run.
export function registerLanguageFeatures(host) {
    if (!editor || !monacoApi || languageRegistered) return;
    languageRegistered = true;

    // (3) Run on Ctrl/Cmd+Enter by clicking the enabled Run button — same handler path as a real click.
    editor.addCommand(monacoApi.KeyMod.CtrlCmd | monacoApi.KeyCode.Enter, () => {
        const run = document.querySelector(".pg-run:not([disabled])");
        if (run) run.click();
    });

    // (2) Live diagnostics: debounce edits, then ask .NET to bind the buffer and paint the markers.
    editor.onDidChangeModelContent(() => {
        clearTimeout(diagnoseTimer);
        diagnoseTimer = setTimeout(runDiagnostics, 400);
    });
    runDiagnostics(); // check the freshly-loaded code once now, without waiting for a keystroke

    // (1) IntelliSense.
    monacoApi.languages.registerCompletionItemProvider("csharp", {
        triggerCharacters: [".", " "],
        async provideCompletionItems(model, position) {
            const offset = model.getOffsetAt(position);
            let items;
            try {
                const json = await window.DotNet.invokeMethodAsync(
                    PLAYGROUND_ASSEMBLY, "PlaygroundComplete", model.getValue(), offset);
                items = JSON.parse(json);
            } catch {
                return { suggestions: [] };
            }

            const word = model.getWordUntilPosition(position);
            const range = {
                startLineNumber: position.lineNumber,
                endLineNumber: position.lineNumber,
                startColumn: word.startColumn,
                endColumn: word.endColumn
            };
            return {
                suggestions: items.map((it) => ({
                    label: it.label,
                    kind: completionKind(it.kind),
                    insertText: it.insertText,
                    sortText: it.sortText,
                    detail: it.detail || undefined,
                    range
                }))
            };
        }
    });
}

async function runDiagnostics() {
    if (!editor || !window.DotNet) return;
    try {
        const json = await window.DotNet.invokeMethodAsync(
            PLAYGROUND_ASSEMBLY, "PlaygroundDiagnose", editor.getValue());
        applyMarkers(json);
    } catch {
        // No live squiggles this pass — swallow so a transient interop hiccup can't wedge the editor.
    }
}

// Paint compiler/analyzer markers (JSON string) onto the editor model. Shared by Run (setMarkers) and the
// live diagnostics loop so both render diagnostics identically.
function applyMarkers(diagnosticsJson) {
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

    monacoApi.editor.setModelMarkers(editor.getModel(), "rask", diagnostics.map((d) => ({
        severity: severity(d.severity),
        message: `${d.id}: ${d.message}`,
        startLineNumber: d.startLine,
        startColumn: d.startColumn,
        endLineNumber: d.endLine,
        endColumn: d.endColumn
    })));
}

// Map Roslyn's primary completion tag onto a Monaco icon kind.
function completionKind(kind) {
    const K = monacoApi.languages.CompletionItemKind;
    switch (kind) {
        case "Method":
        case "ExtensionMethod": return K.Method;
        case "Property": return K.Property;
        case "Field": return K.Field;
        case "Class": return K.Class;
        case "Structure": return K.Struct;
        case "Interface": return K.Interface;
        case "Enum": return K.Enum;
        case "EnumMember": return K.EnumMember;
        case "Delegate": return K.Function;
        case "Event": return K.Event;
        case "Namespace": return K.Module;
        case "Keyword": return K.Keyword;
        case "Local":
        case "Parameter": return K.Variable;
        case "TypeParameter": return K.TypeParameter;
        case "Constant": return K.Constant;
        case "Snippet": return K.Snippet;
        default: return K.Text;
    }
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
