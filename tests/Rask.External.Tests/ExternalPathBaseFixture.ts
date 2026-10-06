// Node-driven fixture for the client runtime under a PATH BASE
// (src/Rask.External/wwwroot/rask-external.js — pathBaseOf, underPathBase).
//
// The runtime learns the app's path base from the URL it was itself loaded from, so it cannot be
// imported the way the other fixtures do it: bundled into this file it would report this file's URL.
// It is read off disk instead (argv[2]) and served to node by a load hook, once from the root and once
// from under /shop — the two deploys of one app. Chunks are served the same way, so what is recorded is
// the address the runtime really asked for.

import {readFileSync} from "node:fs";
import {registerHooks} from "node:module";

const ORIGIN = "https://app.test";
const RUNTIME = "/_content/Rask.External/rask-external.js";
const DEV_CHUNK = "http://localhost:5174/@fs/app/Live.entry.ts";

const runtimeSource = readFileSync(process.argv[2], "utf8");

let imported = [];
let fetched = [];
globalThis.__raskMounted = [];

registerHooks({
    resolve(specifier, context, next) {
        if (!specifier.startsWith(ORIGIN)) return next(specifier, context);

        // Recorded here, not in `load`: node loads a module once, and both deploys can ask for the same one.
        if (!specifier.endsWith(RUNTIME)) imported.push(new URL(specifier).pathname);
        return {url: specifier, shortCircuit: true};
    },
    load(url, context, next) {
        if (!url.startsWith(ORIGIN)) return next(url, context);
        if (url.endsWith(RUNTIME)) return {format: "module", source: runtimeSource, shortCircuit: true};

        const path = new URL(url).pathname;
        const island = JSON.stringify(path.slice(path.lastIndexOf("/") + 1, -".js".length));
        return {
            format: "module",
            source: `export default { mount() { globalThis.__raskMounted.push(${island}); return {}; } }`,
            shortCircuit: true,
        };
    },
});

// Baked at build, so the same table whatever the deploy: root-relative chunks, and one a library wrote
// already under the base.
const manifest = {
    Chart: "/_rask/external/assets/Chart.js",
    Already: "/shop/_rask/external/assets/Already.js",
};

globalThis.fetch = (address) => {
    fetched.push(new URL(address).pathname);
    return Promise.resolve({ok: true, status: 200, json: () => Promise.resolve(manifest)});
};

globalThis.__raskExternalManual = true;

function island(name, manifestUrl) {
    const attrs = {name, props: "{}", manifest: manifestUrl};
    return {isConnected: true, firstChild: null, getAttribute: (n) => attrs[n] ?? null};
}

// One deploy: the runtime served from under `base`, a page with three islands on it.
async function deploy(base) {
    imported = [];
    fetched = [];
    globalThis.__raskMounted = [];
    globalThis.location = new URL(ORIGIN + base + "/dashboard");

    const errors = [];
    const consoleError = console.error;
    console.error = (...args) => errors.push(args.map(String).join(" "));

    const runtime = await import(ORIGIN + base + RUNTIME);
    runtime.__internals.hydrate(island("Chart"));
    runtime.__internals.hydrate(island("Already"));
    runtime.__internals.hydrate(island("Chart", "/_content/Acme.Ui/manifest.json"));
    for (let turn = 0; turn < 6; turn++) {
        await new Promise((r) => setTimeout(r, 0));
    }

    console.error = consoleError;
    return {
        base: runtime.__internals.pathBaseOf(ORIGIN + base + RUNTIME),
        fetched,
        imported,
        mounted: globalThis.__raskMounted,
        errors,
        devChunk: runtime.__internals.underPathBase(DEV_CHUNK),
    };
}

process.stdout.write(JSON.stringify({root: await deploy(""), shop: await deploy("/shop")}) + "\n");
