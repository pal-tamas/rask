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

const {getAssemblyExports, runMain} = await dotnet
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
    .create();

// JSExport Dispatch lives in Rask.Wasm.dll, not the main assembly.
const raskWasmExports = await getAssemblyExports("Rask.Wasm");

const rask = await import('./rask.wasm.js');
rask.setExports(raskWasmExports);

await runMain();
