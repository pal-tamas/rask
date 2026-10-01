// The Rask.* invoke gate both runtimes share: a first-render `Rask.X.method` call is parked until the
// head assets it may depend on have settled and window.Rask.X exists, then dispatched.
//
// CONTRACT: the gate waits for each tracked asset's TERMINAL event — load, error or a timeout — not for
// it to succeed. A failed asset (CDN flake, CSP, integrity mismatch) still opens the gate, so user JS that
// needs the asset's global must be defensive (`if (typeof window.hljs === "undefined") return;`). The
// failure paths log a warning naming the asset, so the resulting TypeError is traceable.
//
// The server and WASM runtimes each carried a copy; only WASM's learned the cold-boot fixes kept here
// (scoped scripts tracked, a long same-origin backstop, parser-inserted scripts trusted), so a slow
// cold load on the server force-faulted the invoke into "Could not find 'Rask.X' on target".

// A cross-origin CDN keeps the short contract: a dead CDN must not hold Rask.* invokes for long.
const HEAD_ASSET_LOAD_TIMEOUT_MS = 5000;
// Same-origin assets (scoped /_rask/a/{hash}.js, a self-hosted vendor script) are reliable but can lag a
// cold boot on a constrained machine. A missing one still fires 'error' at once, so this long window only
// ever applies to an asset that is genuinely still loading.
const SAME_ORIGIN_LOAD_TIMEOUT_MS = 30000;
const NAMESPACE_POLL_INTERVAL_MS = 100;

export interface InvokeGate<T> {
    /** Tracks the head's script/stylesheet assets. `parserInserted` only for the sweep at boot. */
    scanHeadAssets(parserInserted?: boolean): void;
    /** Parks `invoke` when it must wait, and says so; the caller dispatches it otherwise. */
    park(invoke: T): boolean;
    /** Dispatches every parked invoke that is now ready. */
    drain(): void;
}

/**
 * @param identifierOf the invoke's dotted JS identifier (`Rask.CodeSample.rendered`).
 * @param dispatch runs an invoke past the gate — for a namespace that never appeared, its original
 *     "Could not find" error reaches the component's error boundary rather than hanging.
 */
export function createInvokeGate<T>(identifierOf: (invoke: T) => string, dispatch: (invoke: T) => void): InvokeGate<T> {
    // Keyed by ELEMENT, not URL: two <script src> for one URL are two things to wait for.
    const pendingHeadAssets = new Set<Element>();
    const trackedHeadAssets = new WeakSet<Element>();
    let parked: T[] = [];
    let pollHandle = 0;
    let pollStarted = 0;

    const headAssetsReady = (): boolean => pendingHeadAssets.size === 0;

    function isAssetAlreadyLoaded(url: string): boolean {
        if (!url || !window.performance || !performance.getEntriesByName) return false;
        for (const entry of performance.getEntriesByName(url) as PerformanceResourceTiming[]) {
            if (entry.responseEnd > 0) return true;
        }
        return false;
    }

    function trackHeadAsset(el: Element, parserInserted: boolean): void {
        if (el.nodeType !== 1 || trackedHeadAssets.has(el)) return;
        let url: string;
        if (el.tagName === "SCRIPT" && (el as HTMLScriptElement).src) url = (el as HTMLScriptElement).src;
        else if (el.tagName === "LINK" && (el as HTMLLinkElement).rel === "stylesheet" && (el as HTMLLinkElement).href) url = (el as HTMLLinkElement).href;
        else return;

        // Framework-scoped tags carry the reserved "rsk-" key. Scoped CSS defines no JS global, so it stays out;
        // a scoped SCRIPT defines window.Rask.{Type} and is exactly what a first-render invoke waits for.
        const key = el.getAttribute("data-rask-key");
        const isScoped = !!key && key.indexOf("rsk-") === 0;
        if (isScoped && el.tagName !== "SCRIPT") return;
        trackedHeadAssets.add(el);

        // A scoped script the HTML parser put in <head> has already run: deferred scripts execute in document
        // order before the runtime module, so its load event fired before a listener could be attached, and
        // waiting for one parked every invoke until the backstop. The namespace poll still covers a namespace
        // that genuinely is not there.
        if (isScoped && parserInserted) return;
        // Downloaded is not executed, so a scoped script waits for its real load event. For a user's own head
        // asset, "downloaded" is an acceptable proxy (their defensive code is the contract).
        if (!isScoped && isAssetAlreadyLoaded(url)) return;

        pendingHeadAssets.add(el);
        const sameOrigin = url.indexOf(location.origin) === 0;
        const timeout = isScoped || sameOrigin ? SAME_ORIGIN_LOAD_TIMEOUT_MS : HEAD_ASSET_LOAD_TIMEOUT_MS;
        const finish = (outcome: "load" | "error" | "timeout"): void => {
            if (!pendingHeadAssets.delete(el)) return;
            if (outcome !== "load") {
                const reason = outcome === "error"
                    ? "fired 'error' event (network failure / blocked / integrity mismatch / CSP)"
                    : `did not fire load/error within ${timeout}ms — proceeding anyway`;
                console.warn(`[Rask] Head asset (${el.tagName.toLowerCase()}) ${url} ${reason}. ` +
                    "Queued Rask.* invokes will run; user JS depending on this asset's global must be defensive.");
            }
            drain();
        };
        el.addEventListener("load", () => finish("load"), {once: true});
        el.addEventListener("error", () => finish("error"), {once: true});
        // The event may have fired between insertion and this listener (a cache hit); the backstop covers it.
        setTimeout(() => finish("timeout"), timeout);
    }

    // True for a non-Rask identifier; for `Rask.{Name}.{method}`, whether window.Rask.{Name} exists yet.
    function namespaceReady(identifier: string): boolean {
        if (identifier.indexOf("Rask.") !== 0) return true;
        const rest = identifier.substring(5);
        const dot = rest.indexOf(".");
        const name = dot < 0 ? rest : rest.substring(0, dot);
        return !!(window.Rask && window.Rask[name]);
    }

    function drain(): void {
        if (!headAssetsReady() || parked.length === 0) return;
        const ready = parked.filter(invoke => namespaceReady(identifierOf(invoke)));
        parked = parked.filter(invoke => !namespaceReady(identifierOf(invoke)));
        for (const invoke of ready) dispatch(invoke);
    }

    // A parked invoke wakes when its namespace appears. The timeout matches the same-origin backstop, and it
    // force-dispatches only once every tracked asset has settled — a still-loading scoped script drains the
    // queue by its own load event instead of being faulted early on a slow machine.
    function ensureNamespacePoll(): void {
        if (pollHandle !== 0) return;
        pollStarted = Date.now();
        pollHandle = window.setInterval(() => {
            const timedOut = Date.now() - pollStarted > SAME_ORIGIN_LOAD_TIMEOUT_MS;
            if (parked.length === 0 || (timedOut && headAssetsReady())) {
                clearInterval(pollHandle);
                pollHandle = 0;
                const remaining = parked;
                parked = [];
                for (const invoke of remaining) dispatch(invoke);
                return;
            }
            drain();
        }, NAMESPACE_POLL_INTERVAL_MS);
    }

    return {
        scanHeadAssets(parserInserted = false) {
            for (const el of document.head.querySelectorAll("script[src], link[rel=stylesheet]")) {
                trackHeadAsset(el, parserInserted);
            }
        },
        park(invoke) {
            const identifier = identifierOf(invoke);
            if (typeof identifier !== "string" || identifier.indexOf("Rask.") !== 0) return false;
            if (headAssetsReady() && namespaceReady(identifier)) return false;
            parked.push(invoke);
            ensureNamespacePoll();
            return true;
        },
        drain,
    };
}
