// Rask WASM bootstrap. Boots the .NET runtime and hands the assembly exports
// to rask.wasm.js (which .NET also imports via JSHost.ImportAsync) so click /
// input / submit handlers can dispatch into [JSExport] Dispatch.

import {dotnet} from './_framework/dotnet.js';

const {getAssemblyExports, runMain} = await dotnet
    .withApplicationArgumentsFromQuery()
    .create();

// JSExport Dispatch lives in Rask.Wasm.dll, not the main assembly.
const raskWasmExports = await getAssemblyExports("Rask.Wasm");

const rask = await import('./rask.wasm.js');
rask.setExports(raskWasmExports);

await runMain();
