// Scoped-CSS FOUC (flash-of-unstyled-content) gating, shared by every client.
//
// Imported by the Server runtime (rask.ts) and the WASM runtime (rask.wasm.ts). A newly mounted
// component ships its scoped stylesheet as a keyed <link href="/_rask/a/{hash}.css"
// data-rask-key="rsk-…">; without this gate the swapped body paints before that just-inserted sheet
// parses and applies, flashing unstyled. Both entry points return a Promise the host chains its
// render commit on (or null when there is nothing new to wait for, preserving today's single-pass
// timing).
//
// Relies only on `document` and the standard timers — no transport coupling.
//
// NOTE: the scoped-JS `Rask.*` invoke gate (trackHeadAsset / ensureRaskNamespacePoll /
// beginInvokeJS deferral) is deliberately NOT here — it has genuinely diverged between the Server
// (skips rsk- assets, 5s timeout) and WASM (tracks rsk- scripts, 30s backstop) hosts, so it stays
// inline per host until a dedicated reconciliation pass.

// Hard cap on how long a render defers the body swap waiting for a newly mounted page's scoped
// stylesheet to apply. A warm, content-addressed /_rask/a/{hash}.css load resolves in a few ms;
// the cap only ever applies to a genuinely slow/failed sheet, where we'd rather show the (briefly
// unstyled) page than stall navigation.
const CSS_FOUC_GUARD_MS = 500;

// Return a Promise that resolves once every <head> stylesheet still being applied has
// reached a terminal state (load / error / CSS_FOUC_GUARD_MS timeout), or null when
// there's nothing to wait for. The readiness signal is the <link>'s .sheet property —
// non-null only once the CSSOM stylesheet has been parsed and APPLIED. We deliberately
// do NOT use Resource Timing (responseEnd): the eager <link rel="prefetch"> warms the
// HTTP cache and creates a timing entry, but bytes downloaded is not the same as a
// stylesheet applied — trusting it would skip the wait and reintroduce the very flash
// prefetch is meant to remove. A link already applied (kept across renders, or just
// resolved) has a non-null .sheet and is skipped; a freshly inserted one has
// .sheet === null and is awaited (its load fires within ~1 frame on warm cache).
export function waitForUnappliedHeadCss(): Promise<unknown> | null {
    const pending: Promise<void>[] = [];

    document.head.querySelectorAll<HTMLLinkElement>('link[rel="stylesheet"]').forEach((l) => {
        if (!l.href || l.sheet) return;
        pending.push(new Promise<void>((resolve) => {
            const done = (): void => resolve();
            l.addEventListener("load", done, { once: true });
            l.addEventListener("error", done, { once: true });
            setTimeout(done, CSS_FOUC_GUARD_MS);
        }));
    });

    return pending.length ? Promise.all(pending) : null;
}

// FOUC guard for the full-document path. A full reply morphs <head> and the styled <body> in one
// pass, so a newly mounted component's scoped <link> would be inserted alongside the body it styles
// — and the body paints before the just-inserted sheet parses + applies. Pre-empt it: for every NEW
// scoped stylesheet the incoming document adds to <head> (keyed by data-rask-key, so not already
// live), append a clone NOW and return a Promise that resolves once each has applied (.sheet) —
// load / error / CSS_FOUC_GUARD_MS timeout. The subsequent morph matches each clone to the incoming
// <link> by key (keyed reconciliation), so it's kept rather than duplicated, and the body it morphs
// in paints already-styled. Only keyed scoped links are preloaded — render-blocking globals (no
// data-rask-key) are already applied. Returns null when the document adds no new scoped stylesheet
// (the common case), so a navigation that mounts nothing new keeps today's single-pass, no-wait timing.
export function preloadNewHeadStylesheets(freshHtml: ParentNode): Promise<unknown> | null {
    const freshHead = freshHtml.querySelector("head");
    if (!freshHead) return null;

    const liveKeys: Record<string, boolean> = {};
    document.head.querySelectorAll<HTMLLinkElement>('link[rel="stylesheet"][data-rask-key]').forEach((l) => {
        const key = l.getAttribute("data-rask-key");
        if (key) liveKeys[key] = true;
    });

    const pending: Promise<void>[] = [];
    freshHead.querySelectorAll<HTMLLinkElement>('link[rel="stylesheet"][data-rask-key]').forEach((fl) => {
        const key = fl.getAttribute("data-rask-key");
        if ((key && liveKeys[key]) || !fl.getAttribute("href")) return;

        const clone = fl.cloneNode(true) as HTMLLinkElement;
        document.head.appendChild(clone);
        pending.push(new Promise<void>((resolve) => {
            const done = (): void => resolve();
            clone.addEventListener("load", done, { once: true });
            clone.addEventListener("error", done, { once: true });
            setTimeout(done, CSS_FOUC_GUARD_MS);
        }));
    });

    return pending.length ? Promise.all(pending) : null;
}
