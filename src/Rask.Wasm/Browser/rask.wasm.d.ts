// The built WASM client runtime, as main.ts sees it.
//
// `rask.wasm.js` beside this file is generated: esbuild bundles Resources/rask.wasm.ts into it. It is
// a SEPARATE entry point from main.ts rather than something main.ts bundles, because .NET imports it
// too — `JSHost.ImportAsync("rask", "./rask.wasm.js")` — and two copies of the runtime would mean two
// sets of document listeners and two render queues.
//
// So main.ts imports the artifact, not the source, and this declares the artifact's shape.
// TypeScript picks a `.d.ts` up automatically for the `.js` of the same name beside it, which is why
// this is a module rather than a `declare module` block — an ambient one cannot name a relative path.
//
// Only `setExports` is called from main.ts; everything else the module does, it does through
// [JSImport] declarations on the managed side.

/**
 * Hands the runtime the assembly exports, so click / input / submit handlers can dispatch into the
 * `[JSExport] Dispatch` in Rask.Wasm.dll.
 */
export declare function setExports(exports: Record<string, never>): void;
