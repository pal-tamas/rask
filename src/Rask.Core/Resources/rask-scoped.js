// rask-scoped.js — scoped-CSS FOUC (flash-of-unstyled-content) gating, shared by all three clients.
//
// Spliced (at "// @@RASK_SCOPED@@") into the Server runtime (rask.js), the WASM runtime
// (rask.wasm.js). A newly mounted component ships its
// scoped stylesheet as a keyed <link href="/_rask/a/{hash}.css" data-rask-key="rsk-…">; without
// this gate the swapped body paints before that just-inserted sheet parses + applies, flashing
// unstyled. Both entry points return a Promise the host chains its render commit on (or null when
// there's nothing new to wait for, preserving today's single-pass timing).
//
// Relies only on the global `document` + standard timers — no transport coupling. Modern-ES
// (const/let/arrow), matching rask-dom.js / rask-morph.js. No export/import, no backslash regex.
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
function waitForUnappliedHeadCss() {
    const pending = [];
    document.head.querySelectorAll('link[rel="stylesheet"]').forEach((l) => {
        if (!l.href || l.sheet) return;
        pending.push(new Promise((resolve) => {
            const done = () => resolve();
            l.addEventListener("load", done, {once: true});
            l.addEventListener("error", done, {once: true});
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
function preloadNewHeadStylesheets(freshHtml) {
    const freshHead = freshHtml.querySelector("head");
    if (!freshHead) return null;
    const liveKeys = {};
    document.head.querySelectorAll('link[rel="stylesheet"][data-rask-key]').forEach((l) => {
        liveKeys[l.getAttribute("data-rask-key")] = true;
    });
    const pending = [];
    freshHead.querySelectorAll('link[rel="stylesheet"][data-rask-key]').forEach((fl) => {
        if (liveKeys[fl.getAttribute("data-rask-key")] || !fl.getAttribute("href")) return;
        const clone = fl.cloneNode(true);
        document.head.appendChild(clone);
        pending.push(new Promise((resolve) => {
            const done = () => resolve();
            clone.addEventListener("load", done, {once: true});
            clone.addEventListener("error", done, {once: true});
            setTimeout(done, CSS_FOUC_GUARD_MS);
        }));
    });
    return pending.length ? Promise.all(pending) : null;
}
