// Rask WASM bootstrap. Boots the .NET runtime and hands the assembly exports
// to rask.wasm.js (which .NET also imports via JSHost.ImportAsync) so click /
// input / submit handlers can dispatch into [JSExport] Dispatch.

import {dotnet} from './_framework/dotnet.js';

// Boot progress. On a slow link the runtime + assemblies are several MB, and the
// bare splash spinner can't be told apart from a hang. The runtime's built-in
// onDownloadResourceProgress callback reports how many boot resources have
// finished out of the total, which we render as a determinate bar.
//
// Resource-COUNT progress (what the callback gives), not bytes: framework assets
// are commonly served Brotli/gzip precompressed, so byte-based progress would
// have to reconcile encoded vs. decoded sizes; counts sidestep that. The bar
// stays hidden (spinner-only fallback) until the first progress tick arrives.
const boot = document.querySelector(".rask-boot");
const bootProgress = boot?.querySelector(".rask-boot__progress");
const bootFill = boot?.querySelector(".rask-boot__fill");
const bootLabel = boot?.querySelector(".rask-boot__label");

function renderBootProgress(loaded, total) {
    if (!bootProgress || !(total > 0)) return;
    bootProgress.hidden = false;
    const pct = Math.min(100, Math.round((loaded / total) * 100));
    if (bootFill) bootFill.style.width = `${pct}%`;
    if (bootLabel) bootLabel.textContent = `Loading… ${pct}%`;
}

// ---------------------------------------------------------------------------
// Boot failure reporting.
//
// A WASM app that downloads its runtime and then fails to mount used to leave the
// splash spinner turning for ever: no console error, no page error, nothing on
// screen. A 404 on _framework, an import-map/SRI drift, an empty scoped-asset bake
// and a genuinely slow network all presented identically, which is what made every
// occurrence expensive to diagnose (#817).
//
// The markup and CSS live HERE rather than in the shell's index.html on purpose.
// There are several shells — the framework's, the samples', and the one `rask new`
// writes — and they have already drifted from one another; a user is free to write
// their own. Owning the failure surface in the one file every one of them loads
// means it cannot be missing from any of them, including shells that predate it.
const BOOT_ERROR_CSS = `
.rask-boot[data-rask-boot-error] { gap: 0.75rem; padding: 1.5rem; }
.rask-boot[data-rask-boot-error] svg,
.rask-boot[data-rask-boot-error] .rask-spin,
.rask-boot[data-rask-boot-error] .rask-boot__progress { display: none; }
.rask-boot__error {
    max-width: 46rem;
    text-align: left;
    font-family: system-ui, -apple-system, "Segoe UI", Roboto, sans-serif;
    color: #7f1d1d;
    background: #fef2f2;
    border: 1px solid #fecaca;
    border-radius: 0.75rem;
    padding: 1.25rem 1.5rem;
}
.rask-boot__error h1 { margin: 0 0 0.5rem; font-size: 1.0625rem; font-weight: 600; }
.rask-boot__error p { margin: 0 0 0.75rem; font-size: 0.875rem; line-height: 1.5; color: #991b1b; }
.rask-boot__error pre {
    margin: 0;
    padding: 0.75rem;
    max-height: 16rem;
    overflow: auto;
    font-size: 0.75rem;
    line-height: 1.5;
    white-space: pre-wrap;
    word-break: break-word;
    background: #fff;
    border: 1px solid #fecaca;
    border-radius: 0.5rem;
    color: #450a0a;
}
.rask-boot__error-hint { margin: 0.75rem 0 0; font-size: 0.8125rem; opacity: 0.85; }
`;

let bootFailureReported = false;

/**
 * Render a boot failure where the visitor is already looking, and say what happened.
 *
 * Called from three places: this module's own catch, rask.wasm.js when the very first
 * frame is unusable, and .NET via the `bootFailed` JSImport — the managed side knows
 * far more about a managed exception than the JS side can infer from a rejection.
 *
 * @param {string} message  one line naming what failed
 * @param {string} [detail] stack / exception text, shown verbatim
 */
function reportBootFailure(message, detail) {
    // Always log, whatever else happens: the console line is what someone reading a CI
    // artefact will find, and it is the only channel left if the DOM work below throws.
    console.error(`[Rask] boot failed: ${message}`, detail ?? "");

    // Once the app has painted, a later failure belongs to the running app rather than to boot, and the
    // root error boundary owns it — never paint a full-screen failure over a working page.
    //
    // Read from a flag rask.wasm.js sets when it applies a frame, NOT from whether the splash element is
    // still in the document. The morph patches the existing document in place, so that element stays
    // connected after a perfectly good first render; believing otherwise made every WASM journey report
    // a boot failure against an app whose console said "first render applied".
    if (globalThis.__raskPainted) return;
    if (!boot?.isConnected) return;
    // First failure wins. A boot failure usually cascades (the throw, then the rejection that
    // follows it, then the never-painted check below), and the first one is the cause.
    if (bootFailureReported) return;
    bootFailureReported = true;

    try {
        const style = document.createElement("style");
        style.textContent = BOOT_ERROR_CSS;
        document.head.appendChild(style);

        const panel = document.createElement("div");
        panel.className = "rask-boot__error";

        const heading = document.createElement("h1");
        heading.textContent = "This app failed to start.";
        panel.appendChild(heading);

        const summary = document.createElement("p");
        summary.textContent = message;
        panel.appendChild(summary);

        if (detail) {
            const pre = document.createElement("pre");
            // textContent, not innerHTML: this string is an exception message, and it can carry
            // anything the runtime put in it.
            pre.textContent = detail;
            panel.appendChild(pre);
        }

        const hint = document.createElement("p");
        hint.className = "rask-boot__error-hint";
        hint.textContent = "The browser console has the full error.";
        panel.appendChild(hint);

        boot.replaceChildren(panel);
        // The stable hook an E2E fixture waits on, so a broken boot fails a test in seconds with a
        // reason attached instead of timing out on a selector that is never going to appear.
        boot.setAttribute("data-rask-boot-error", "");
    } catch (e) {
        console.error("[Rask] boot failed, and rendering the failure also failed", e);
    }
}

function describe(error) {
    if (error instanceof Error) return error.stack || `${error.name}: ${error.message}`;
    return String(error);
}

// Exposed before the first await, so a failure inside dotnet.create() can already reach it, and
// so rask.wasm.js and the managed side share this one implementation rather than each growing
// half of one.
globalThis.__raskBootFailed = reportBootFailure;

// A module's top-level await rejects into the unhandled-rejection channel, and the runtime starts
// work of its own that can fail after create() has resolved. Both land here.
globalThis.addEventListener("unhandledrejection", event => {
    reportBootFailure("An unhandled error occurred while starting.", describe(event.reason));
});
globalThis.addEventListener("error", event => {
    // Only script errors carry something worth showing. Resource load errors bubbling from
    // elements have no message on them, and the runtime reports the ones that matter itself.
    if (event.error) reportBootFailure("An unhandled error occurred while starting.", describe(event.error));
});

// Each step names itself, because which one failed is most of the diagnosis: the runtime not
// downloading is a serving problem, a missing export is a build problem, and a throw out of
// runMain is the app's own.
async function step(what, run) {
    try {
        return await run();
    } catch (error) {
        reportBootFailure(what, describe(error));
        throw error;
    }
}

const {getAssemblyExports, runMain} = await step(
    "The .NET runtime could not be loaded. Check that the _framework assets are being served, "
    + "with the correct application/wasm content type.",
    () => dotnet
        .withApplicationArgumentsFromQuery()
        .withModuleConfig({
            onDownloadResourceProgress: (resourcesLoaded, totalResources) => {
                // Best-effort UI; never let a progress hiccup break boot.
                try {
                    renderBootProgress(resourcesLoaded, totalResources);
                } catch (e) {
                }
            }
        })
        .create());

// JSExport Dispatch lives in Rask.Wasm.dll, not the main assembly.
const raskWasmExports = await step(
    "The Rask.Wasm assembly exports could not be read.",
    () => getAssemblyExports("Rask.Wasm"));

const rask = await step(
    "The Rask browser module (rask.wasm.js) could not be loaded.",
    () => import('./rask.wasm.js'));
rask.setExports(raskWasmExports);

await step("The app threw while starting.", () => runMain());

// runMain resolves only once Program.cs's `await host.RunAsync<App>()` has returned, and the first frame
// is pushed synchronously from inside it — so by now a frame has been applied and rask.wasm.js has set
// this flag. Its absence means the app finished starting without ever painting, which is otherwise
// indistinguishable from a hang.
//
// Asked of the render path rather than of the DOM. The obvious-looking test — "is the splash element
// still in the document" — is wrong, because the morph patches the document in place and leaves it
// connected, so it reports a boot failure for every successful boot.
// __raskPrepared is the takeover case: the app booted deliberately WITHOUT painting, because another
// runtime is still driving this document. That is a successful start, not a silent hang.
if (!globalThis.__raskPainted && !globalThis.__raskPrepared) {
    reportBootFailure(
        "The app finished starting but never rendered. Check that Program.cs awaits "
        + "host.RunAsync<App>() and that the app has a route for this URL.");
}
