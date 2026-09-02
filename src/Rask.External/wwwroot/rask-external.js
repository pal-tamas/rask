// rask-external.js — the browser half of Rask.External.
//
// Finds the host elements the server rendered, loads each island's chunk, and mounts it under the
// hydration policy the C# asked for. Deliberately knows nothing about React, Lit or any other
// framework: an island's built chunk default-exports its own ADAPTER — three functions, mount /
// update / unmount — so adding a runtime never touches this file.
//
// It also owns the two halves of the boundary that are easy to get wrong: turning handler references
// back into functions with STABLE identity, and routing a changed props attribute to the adapter
// instead of letting it land as an attribute nobody reads.

const HOST_TAG = "RASK-EXTERNAL";
const MANIFEST_URL = "/_rask/external/manifest.json";

/** element -> {adapter, handle, fns, name} for everything currently mounted. */
const mounted = new WeakMap();

/** Cached manifest fetch. One request per page however many islands are on it. */
let manifest = null;

/** Cached @vite/client import. One per page, and only under `rask dev`. */
let hmrClient = null;

/**
 * Where the islands' Vite dev server is, or null outside `rask dev`.
 *
 * Stamped on <body> by the server, the same way data-rask-dev-status and data-rask-wasm are. Read
 * lazily rather than at module scope: this file is a module in <head>, so <body> may not exist yet.
 */
function devServer() {
    return (typeof document !== "undefined" && document.body
        && document.body.getAttribute("data-rask-islands-dev")) || null;
}

/**
 * Loads Vite's HMR client, once, when `rask dev` is running an island dev server.
 *
 * This is the whole of Rask's involvement in hot module replacement, and deliberately so. Once
 * @vite/client is on the page and the island chunks are being served BY the dev server, each
 * framework's own refresh integration takes over the modules it owns — React Fast Refresh, Solid's
 * refresh transform, Vue's and Svelte's plugin HMR. Rask replacing a mounted island itself would
 * fight those and lose: they preserve component state, and a remount is exactly what the diff
 * boundary exists to avoid.
 *
 * A runtime with no refresh integration — Lit, whose custom elements cannot be re-registered, and
 * Angular — falls back to the full page reload @vite/client performs on its own. Slower, still no
 * rebuild of the C# app.
 *
 * Failure is not fatal: without it the island still loads from the dev server, it just will not
 * hot-replace.
 */
function ensureHmrClient(origin) {
    hmrClient ??= import(/* @vite-ignore */ origin + "/@vite/client").catch((error) => {
        console.warn(
            "Rask islands: the dev server is configured but @vite/client did not load, so islands " +
            "will not hot-replace. Is `rask dev` still running?", error);
    });

    return hmrClient;
}

/**
 * How an island name becomes a module. Overridable so the runtime can be driven with no bundler and
 * no network — which is what the node fixture does, and what a test harness would do.
 */
function resolver() {
    return (globalThis.__raskExternal && globalThis.__raskExternal.resolve) || defaultResolve;
}

async function defaultResolve(name) {
    manifest ??= fetch(MANIFEST_URL, {credentials: "same-origin"})
        .then((r) => (r.ok ? r.json() : Promise.reject(new Error(`islands manifest: HTTP ${r.status}`))));

    // ONE resolution path in dev and in production. The manifest is the only thing that differs: under
    // `rask dev` the build writes absolute dev-server URLs into it instead of hashed chunk paths, so
    // nothing here has to branch, and the branch that would have existed cannot rot in production.
    const origin = devServer();
    if (origin) {
        await ensureHmrClient(origin);
    }

    const table = await manifest;
    const url = table[name];
    if (!url) {
        throw new Error(
            `Rask islands: '${name}' is not in the manifest. The build writes one entry per island; ` +
            "a missing one usually means the front-end file was added without a rebuild.");
    }

    return import(/* @vite-ignore */ url);
}

/** The dispatch channel the host runtime published. Absent until the runtime has booted. */
function hostSend(payload) {
    const host = globalThis.__raskHost;
    if (!host || typeof host.send !== "function") {
        // Not fatal, and not silent. A callback fired before the live runtime connected is a real
        // event that went nowhere, and saying so beats a UI that simply does not respond.
        console.error("Rask islands: a callback fired before the Rask runtime was ready.", payload);
        return;
    }

    host.send(payload);
}

/**
 * Replaces every {"$h": id} in the props with a real function.
 *
 * `cache` is keyed by handler id and survives across updates, so the SAME function object is handed
 * back for the same id. That is not a micro-optimisation: React compares props by identity, so a
 * fresh closure per update invalidates every useCallback and memo keyed on the callback and re-fires
 * every useEffect that lists it — a performance bug that reads as the framework misbehaving.
 */
function revive(value, cache) {
    if (value === null || typeof value !== "object") return value;

    if (Array.isArray(value)) {
        for (let i = 0; i < value.length; i++) value[i] = revive(value[i], cache);
        return value;
    }

    const id = value.$h;
    if (typeof id === "string" && Object.keys(value).length === 1) {
        let fn = cache.get(id);
        if (!fn) {
            fn = (...args) => hostSend({id, type: "external", args});
            cache.set(id, fn);
        }
        return fn;
    }

    for (const key of Object.keys(value)) value[key] = revive(value[key], cache);
    return value;
}

function readProps(element, cache) {
    const raw = element.getAttribute("props");
    if (!raw) return {};

    try {
        return revive(JSON.parse(raw), cache);
    } catch (error) {
        console.error(`Rask islands: '${element.getAttribute("name")}' has unreadable props.`, error);
        return {};
    }
}

/**
 * Runs `mount` when the element's hydration policy says so.
 *
 * Returns a teardown that cancels a mount still waiting, so an island removed before it was ever
 * visible does not mount into a detached element afterwards.
 */
function schedule(element, mount) {
    const policy = element.getAttribute("hydrate") || "load";

    if (policy === "none") {
        // Server markup only. Nothing is fetched, so an island that ships no JavaScript really ships
        // none — the chunk is never even requested.
        return () => {};
    }

    if (policy === "idle") {
        const handle = globalThis.requestIdleCallback
            ? globalThis.requestIdleCallback(mount)
            : setTimeout(mount, 1);
        return () => (globalThis.cancelIdleCallback ?? clearTimeout)(handle);
    }

    if (policy === "visible") {
        if (typeof IntersectionObserver !== "function") {
            mount();
            return () => {};
        }

        const observer = new IntersectionObserver((entries) => {
            for (const entry of entries) {
                if (!entry.isIntersecting) continue;
                observer.disconnect();
                mount();
                return;
            }
        });
        observer.observe(element);
        return () => observer.disconnect();
    }

    mount();
    return () => {};
}

async function hydrate(element) {
    if (mounted.has(element)) return;

    const name = element.getAttribute("name");
    if (!name) return;

    const cache = new Map();
    // Claimed before the await so a second sweep — a morph, another MutationObserver batch — cannot
    // start a concurrent mount of the same element while the chunk is still loading.
    const entry = {adapter: null, handle: null, fns: cache, name};
    mounted.set(element, entry);

    let cancel = () => {};
    const start = async () => {
        try {
            const module = await resolver()(name, element.getAttribute("module"));
            const adapter = module.default ?? module.adapter;
            if (!adapter || typeof adapter.mount !== "function") {
                throw new Error(
                    `'${name}' loaded, but its chunk does not default-export an adapter. The build wraps ` +
                    "each island with its runtime's adapter; a hand-written entry has to do the same.");
            }

            // Removed while the chunk was in flight. Mounting now would attach a component to a
            // detached element and leak it: nothing would ever unmount it.
            if (!element.isConnected) {
                mounted.delete(element);
                return;
            }

            entry.adapter = adapter;
            entry.handle = adapter.mount(element, readProps(element, cache));
        } catch (error) {
            mounted.delete(element);
            console.error(`Rask islands: '${name}' failed to mount.`, error);
        }
    };

    cancel = schedule(element, start);
    entry.cancel = cancel;
}

function update(element) {
    const entry = mounted.get(element);
    if (!entry || !entry.adapter || typeof entry.adapter.update !== "function") return;

    entry.handle = entry.adapter.update(entry.handle, readProps(element, entry.fns)) ?? entry.handle;
}

function unmount(element) {
    const entry = mounted.get(element);
    if (!entry) return;

    mounted.delete(element);
    entry.cancel?.();

    try {
        entry.adapter?.unmount?.(entry.handle);
    } catch (error) {
        // Teardown must not throw: the element is going away regardless, and an adapter that fails to
        // clean up should not stop the ones after it in the same batch.
        console.error(`Rask islands: '${entry.name}' failed to unmount.`, error);
    }
}

function sweep(root) {
    if (!root || root.nodeType !== 1) return;
    if (root.tagName === HOST_TAG) hydrate(root);
    root.querySelectorAll?.(HOST_TAG.toLowerCase()).forEach(hydrate);
}

function teardown(root) {
    if (!root || root.nodeType !== 1) return;
    if (root.tagName === HOST_TAG) unmount(root);
    root.querySelectorAll?.(HOST_TAG.toLowerCase()).forEach(unmount);
}

/**
 * Watches the document for islands appearing, leaving, or changing props.
 *
 * The props attribute is how a re-render crosses the diff boundary: Rask's diff emits a single
 * SetAttribute for it and nothing else, because the subtree below is opaque. Catching it here and
 * routing it to the adapter is what turns that attribute back into a prop change.
 */
export function start(doc = document) {
    sweep(doc.body ?? doc);

    const observer = new MutationObserver((records) => {
        for (const record of records) {
            if (record.type === "attributes") {
                update(record.target);
                continue;
            }

            record.removedNodes.forEach(teardown);
            record.addedNodes.forEach(sweep);
        }
    });

    observer.observe(doc.body ?? doc, {
        childList: true,
        subtree: true,
        attributes: true,
        attributeFilter: ["props"],
    });

    return () => observer.disconnect();
}

// Exported for tests and for a host that wants to drive the runtime itself.
export const __internals = {revive, schedule, readProps, hydrate, update, unmount, sweep, teardown, devServer};

if (typeof document !== "undefined" && !globalThis.__raskExternalManual) {
    start();
}
