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

/**
 * Where an app's own islands publish their manifest. Used when a host element names no other one,
 * which is every island owned by the app being served.
 */
const DEFAULT_MANIFEST_URL = "/_rask/external/manifest.json";

/** element -> {adapter, handle, fns, name} for everything currently mounted. */
const mounted = new WeakMap();

/**
 * Manifest URL -> Promise of its table. One request per manifest per page, however many islands
 * resolve through it.
 *
 * Keyed rather than a single cached promise, because a page can legitimately have MORE THAN ONE
 * manifest. A class library that owns islands publishes its bundle under its own
 * `_content/<PackageId>/` base, so its manifest is a different document from the app's -- and both
 * can be on screen at once. A single cached fetch made the first manifest to load the only one that
 * existed, and every island from the other library failed to resolve.
 */
const manifests = new Map();

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
 * hot-replace. It is also not remembered — the cache is CLEARED on failure, because the likeliest
 * failure is a race rather than a misconfiguration: `rask dev` waits for the build to name the dev
 * server's config before starting Vite, and the app is already serving pages by then. A page loaded
 * in that window would otherwise never hot-replace for the rest of its life.
 */
function ensureHmrClient(origin) {
    hmrClient ??= import(/* @vite-ignore */ origin + "/@vite/client").catch((error) => {
        hmrClient = null;
        console.warn(
            "Rask islands: the dev server is configured but @vite/client did not load, so islands " +
            "will not hot-replace yet. Is `rask dev` still starting?", error);
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

async function defaultResolve(name, _module, manifestUrl) {
    const url = manifestUrl || DEFAULT_MANIFEST_URL;

    if (!manifests.has(url)) {
        manifests.set(url, fetch(url, {credentials: "same-origin"})
            .then((r) => (r.ok ? r.json() : Promise.reject(new Error(`islands manifest: HTTP ${r.status}`)))));
    }

    const manifest = manifests.get(url);

    // ONE resolution path in dev and in production. The manifest is the only thing that differs: under
    // `rask dev` the build writes absolute dev-server URLs into it instead of hashed chunk paths, so
    // nothing here has to branch, and the branch that would have existed cannot rot in production.
    const origin = devServer();
    if (origin) {
        await ensureHmrClient(origin);
    }

    const table = await manifest;
    const chunk = table[name];
    if (!chunk) {
        throw new Error(
            `Rask islands: '${name}' is not in the manifest at ${url}. The build writes one entry per ` +
            "island; a missing one usually means the front-end file was added without a rebuild, or " +
            "that the island is owned by a library whose manifest attribute did not reach the host " +
            "element.");
    }

    return import(/* @vite-ignore */ chunk);
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
 * Replaces every {"$h": id} in the props with a real function, and every {"$d": iso} with a Date.
 *
 * `cache` is keyed by handler id and survives across updates, so the SAME function object is handed
 * back for the same id. That is not a micro-optimisation: React compares props by identity, so a
 * fresh closure per update invalidates every useCallback and memo keyed on the callback and re-fires
 * every useEffect that lists it — a performance bug that reads as the framework misbehaving.
 *
 * `$a` lists the argument positions that cross back to C#. A package component's first argument is
 * usually a DOM or synthetic event, which holds `view: window` — a cycle — so forwarding everything
 * would throw inside the host's JSON.stringify and the call would be lost. A handler without `$a`
 * forwards every argument, as it always has.
 */
function revive(value, cache) {
    if (value === null || typeof value !== "object") return value;

    if (Array.isArray(value)) {
        for (let i = 0; i < value.length; i++) value[i] = revive(value[i], cache);
        return value;
    }

    const keys = Object.keys(value);
    const id = value.$h;
    if (typeof id === "string" && keys.every((key) => key === "$h" || key === "$a")) {
        let fn = cache.get(id);
        if (!fn) {
            const pick = Array.isArray(value.$a) ? value.$a : null;
            fn = pick
                ? (...args) => hostSend({id, type: "external", args: pick.map((i) => (i < args.length ? args[i] : null))})
                : (...args) => hostSend({id, type: "external", args});
            cache.set(id, fn);
        }
        return fn;
    }

    // Tagged by the C# that declared a date, so exactly those values become Dates — never a string that
    // merely looks like a timestamp, which a reviver guessing from the text would convert silently.
    if (keys.length === 1 && typeof value.$d === "string") return new Date(value.$d);

    for (const key of keys) value[key] = revive(value[key], cache);
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
 * An island's `$c`, split out of its revived props into the tree its adapter renders: text stays a string, a child
 * island becomes `{name, key, manifest, component, props, children}`. Revived before splitting, so a child's callbacks
 * share the host's handler cache — their ids are unique across the page, exactly like the host's own.
 *
 * `children` is null when C# sent no `$c`, which is every island that was never given any.
 */
function splitChildren(props) {
    const raw = props.$c;
    if (!Array.isArray(raw)) return {props, children: null};

    delete props.$c;
    const children = [];
    for (const value of raw) {
        const node = toNode(value);
        if (node !== null) children.push(node);
    }

    return {props, children};
}

function toNode(value) {
    if (typeof value === "string") return value;
    if (!value || typeof value !== "object" || typeof value.n !== "string") return null;

    const {props, children} = splitChildren(value.p && typeof value.p === "object" ? value.p : {});
    return {
        name: value.n,
        key: typeof value.k === "string" ? value.k : null,
        manifest: typeof value.m === "string" ? value.m : null,
        component: null,
        props,
        children,
    };
}

// A child island's framework component, by manifest and name: the promise while its chunk loads, the component once it
// has. Shared across every island on the page, so ten cards in a list fetch the card's chunk once.
const componentsLoading = new Map();
const componentsLoaded = new Map();

function componentKey(node) {
    return `${node.manifest ?? ""}|${node.name}`;
}

function loadComponent(node) {
    const key = componentKey(node);
    let pending = componentsLoading.get(key);
    if (!pending) {
        pending = resolver()(node.name, null, node.manifest).then((module) => {
            // Every built entry exports its framework component beside the adapter. One built before islands could take
            // children exports only the adapter, and that has to be said rather than rendered as nothing.
            if (!module || !("component" in module)) {
                throw new Error(
                    `'${node.name}' is used as a child, but its chunk exports no component — it was built before ` +
                    "islands could take children. Rebuild the app.");
            }

            componentsLoaded.set(key, module.component);
            return module.component;
        });
        componentsLoading.set(key, pending);
        // A failed load is not kept: the next render tries again rather than failing for the rest of the session.
        pending.catch(() => componentsLoading.delete(key));
    }

    return pending;
}

/** Fills in every child island's component from what has already loaded; true when nothing is left to fetch. */
function fillLoaded(nodes) {
    let complete = true;
    for (const node of nodes ?? []) {
        if (typeof node === "string") continue;
        node.component ??= componentsLoaded.get(componentKey(node)) ?? null;
        if (node.component === null) complete = false;
        if (!fillLoaded(node.children)) complete = false;
    }

    return complete;
}

/** Loads every child island's component, in parallel. */
async function loadTree(nodes) {
    if (fillLoaded(nodes)) return;

    await Promise.all((nodes ?? []).map(async (node) => {
        if (typeof node === "string") return;
        node.component ??= await loadComponent(node);
        await loadTree(node.children);
    }));
}

/** What an adapter is handed: the children when there are some, and nothing at all otherwise. */
function childrenArgument(children) {
    return children && children.length > 0 ? children : undefined;
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
    const entry = {adapter: null, handle: null, fns: cache, name, seq: 0};
    mounted.set(element, entry);

    let cancel = () => {};
    const start = async () => {
        try {
            const module = await resolver()(
                name, element.getAttribute("module"), element.getAttribute("manifest"));
            const adapter = module.default ?? module.adapter;
            if (!adapter || typeof adapter.mount !== "function") {
                throw new Error(
                    `'${name}' loaded, but its chunk does not default-export an adapter. The build wraps ` +
                    "each island with its runtime's adapter; a hand-written entry has to do the same.");
            }

            // The child islands' chunks load before the mount, and the props are read again if C#
            // re-rendered while they did: mounting with what was read first would render the island one
            // state behind, with no update left to correct it. An island without children loads nothing.
            let tree;
            for (;;) {
                const raw = element.getAttribute("props");
                tree = splitChildren(readProps(element, cache));
                await loadTree(tree.children);
                if (element.getAttribute("props") === raw) break;
            }

            // Removed while the chunk was in flight. Mounting now would attach a component to a
            // detached element and leak it: nothing would ever unmount it.
            if (!element.isConnected) {
                mounted.delete(element);
                return;
            }

            entry.adapter = adapter;
            entry.handle = adapter.mount(element, tree.props, childrenArgument(tree.children));
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

    const seq = ++entry.seq;
    const {props, children} = splitChildren(readProps(element, entry.fns));
    const apply = () => {
        // A newer update started while this one's children loaded, or the island went away: drop this one, or the
        // older props would land last and stay.
        if (seq !== entry.seq || mounted.get(element) !== entry) return;
        entry.handle = entry.adapter.update(entry.handle, props, childrenArgument(children)) ?? entry.handle;
    };

    // Synchronous whenever every child's chunk has already loaded — the ordinary re-render — so an update that needs
    // no fetch lands in the same turn it always did.
    if (fillLoaded(children)) {
        apply();
    } else {
        loadTree(children).then(apply, (error) => console.error(`Rask islands: '${entry.name}' could not load a child.`, error));
    }
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

    // <html>, NOT <body>, and that difference is the whole of #1035.
    //
    // A MutationObserver watches the NODE it was given. A full-frame render replaces <body> outright
    // rather than patching it, so an observer bound to the body the page loaded with is left holding a
    // detached node: it never fires again, and no island in the new body is ever hydrated. The page
    // then shows empty <rask-external> hosts for the rest of its life.
    //
    // It went unnoticed because it is a race the shell happened to win. When the first response is the
    // boot shell, this module's start() runs before there is anything to mount, WebAssembly swaps in the
    // real body, and the islands arrive INSIDE that new body — so the observer that matters is the one
    // attached afterwards. Serve the same page prerendered and the order inverts: the islands are in the
    // first response, they mount, and then the swap throws them away with the body they were in.
    //
    // documentElement outlives the swap, and `subtree: true` reaches the same nodes it did before —
    // plus the replacement body itself, which arrives as an addedNode and sweeps normally. The removed
    // body sweeps out through teardown, so the discarded islands get their adapter's unmount rather
    // than being dropped on the floor still mounted.
    observer.observe(doc.documentElement ?? doc.body ?? doc, {
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
