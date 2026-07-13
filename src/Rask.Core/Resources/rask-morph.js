// Shared client-side morph algorithm consumed by both rask.js (Server) and
// rask.wasm.js (WASM). Concatenated into each runtime at build time — see the
// MSBuild "_RaskBuildClientJs" target in Rask.Server.csproj and Rask.Wasm.csproj.
//
// Why concat instead of import / network split:
//  - rask.js is a classic <script> served from /rask/rask.js (no ES-module hook).
//  - rask.wasm.js is loaded by JSHost.ImportAsync as an ES module.
// Concat sidesteps the loader mismatch and keeps the single-file delivery model.
//
// Modern JS is fine here — both runtimes target current browsers (the codec uses
// moveBefore / crypto.randomUUID). Two splice constraints, not a dialect one:
//  - The top-level helpers stay hoisted `function` declarations, NOT `const fn =
//    () => …`: applyDiff (rask-dom.js) calls reviveScript() / raskShouldSuppressValue()
//    here, and the two files concatenate into one scope in EITHER order, so the
//    cross-references must resolve regardless of splice ordering (hoisting). Locals,
//    callbacks, and literals inside them use modern syntax freely.
//  - No `export` / `import`: this island is spliced inside the Server's classic-script
//    IIFE, where module syntax is illegal.

// Scripts produced by DOMParser have their "already started" flag set, so the
// browser silently skips them when morph() appends them into the live document.
// Rebuild script nodes via createElement so they actually execute, propagate
// every attribute (type=module, defer, integrity, nonce, crossorigin, …), and
// fire raskAfterMorph again once external scripts finish loading — inline
// scripts run synchronously on insertion and may early-return if they depend
// on a not-yet-loaded global like window.hljs.
function reviveScript(node) {
    if (!node || node.nodeType !== 1 || node.tagName !== "SCRIPT") return node;
    const s = document.createElement("script");
    for (const a of node.attributes) s.setAttribute(a.name, a.value);
    if (s.src) {
        s.async = false;
        s.addEventListener("load", () => {
            if (typeof window.raskAfterMorph === "function") window.raskAfterMorph();
        }, {once: true});
    }
    s.text = node.textContent;
    return s;
}

// Wrappers around the underlying DOM mutation primitives. Scoped-JS hooks are
// not auto-fired by morph — C# components drive invocations explicitly via
// `IJSRuntime.InvokeVoidAsync("Rask.{TypeName}.{method}", ...args)` from a
// lifecycle hook (typically OnRenderedAsync). Calls land in RaskJSRuntime
// (Server) or WasmJSRuntime (WASM), are dispatched against the freshly-morphed
// DOM, and Rask.*-prefixed identifiers are gated by a pending queue so calls
// that race the scoped-JS bundle drain after it loads. If a component needs
// teardown on element removal, install a MutationObserver inside the hook or
// expose an explicit "removed" method and call it from OnUnmount.
function _raskInsertBefore(parent, dst, anchor) {
    parent.insertBefore(dst, anchor);
}

// Relocate an already-attached child before `anchor`. Prefer the Atomic Move API
// (moveBefore, Chromium 133+): it moves the node WITHOUT disconnecting it, so a
// focused descendant keeps focus, selection, and caret across a keyed reorder. A
// plain insertBefore of a connected node still disconnects it briefly and blurs it.
function _raskMoveBefore(parent, node, anchor) {
    if (parent.moveBefore) {
        try {
            parent.moveBefore(node, anchor);
            return;
        } catch (e) {
            // Not connected / cross-document — fall through to insertBefore.
        }
    }
    parent.insertBefore(node, anchor);
}

function _raskAppendChild(parent, dst) {
    parent.appendChild(dst);
}

function _raskRemoveChild(parent, src) {
    parent.removeChild(src);
}

function _raskReplaceChild(parent, dst, src) {
    parent.replaceChild(dst, src);
}

// Lagging-render value guard. When a user commits a change on a change-only input
// (date / number / select), a re-render the server computed BEFORE that change
// reached it can land afterwards and clobber the user's value. The focus guard in
// morph() only protects the *focused* element, but a change commits on blur, so by
// the time the lagging frame arrives focus has already moved on.
//
// On the change dispatch the runtime records the input's PRE-EDIT value (its last
// server-rendered `value` attribute) — exactly what such a lagging frame carries.
// A subsequent server value is suppressed only while it equals that recorded value;
// any other value is the authoritative response to the user's change — the echo of
// the new value OR a server correction/normalisation (e.g. clearing a non-nullable
// int snaps the model to 0) — so it applies and releases the guard. Recording the
// pre-edit value (not the user's new value) is what lets a correction through:
// suppress-if-equal-to-stale, not suppress-unless-equal-to-mine.
//
// Keyed by element identity — morph patches inputs in place, so identity survives
// across re-renders. Backed by a window global so the helper is reachable from both
// the spliced morph (here) and the host runtime's event / diff code (rask.js,
// rask.wasm.js), regardless of splice ordering.
function _raskPendingValues() {
    return window.__raskPendingValues || (window.__raskPendingValues = new WeakMap());
}

function raskNotePendingValue(el, supersededValue) {
    if (el) _raskPendingValues().set(el, supersededValue);
}

function raskShouldSuppressValue(el, incoming) {
    const map = _raskPendingValues();
    if (!el || !map.has(el)) return false;
    if (map.get(el) === incoming) return true;   // lagging frame carrying the stale value
    map.delete(el);                               // authoritative response — release the guard
    return false;
}

// The `.checked` analogue of the value guard above. A native radio/checkbox click flips the
// `.checked` PROPERTY but leaves the `checked` ATTRIBUTE untouched, so the change dispatch records
// the pre-click attribute state (raskNotePendingChecked) — exactly as the value guard records the
// pre-edit `value` attribute. A lagging frame the server computed BEFORE the click reached it still
// carries that stale checked, so it's suppressed until an authoritative frame (the echo of the new
// state OR a server correction) arrives with a different value and releases the guard. For a radio
// the dispatch records the whole same-name group, so a stale frame can't re-check the previously
// selected radio (which would natively uncheck the new one). Kept a hoisted `function` so the
// spliced rask-dom.js can call it regardless of splice ordering — same rationale as the value guard.
function _raskPendingChecked() {
    return window.__raskPendingChecked || (window.__raskPendingChecked = new WeakMap());
}

function raskNotePendingChecked(el, supersededChecked) {
    if (el) _raskPendingChecked().set(el, !!supersededChecked);
}

function raskShouldSuppressChecked(el, incoming) {
    const map = _raskPendingChecked();
    if (!el || !map.has(el)) return false;
    if (map.get(el) === !!incoming) return true;   // lagging frame carrying the stale checked
    map.delete(el);                                 // authoritative response — release the guard
    return false;
}

// Third-party head preservation. Libraries commonly inject <style>/<link>/<script> into <head> at
// runtime (Monaco's theme colours, Chart.js, syntax highlighters, analytics). Those nodes aren't in the
// .NET-rendered head, so a naive reconcile would trim them on the next render — the framework already
// exposes data-rask-managed to opt a node out, but foreign libraries can't be expected to tag what they
// inject. Instead the morph tags every head node it PRODUCES (a __raskHead property set inline as each
// rendered node is placed) and, on later head morphs, skips any head element it never produced — leaving
// the foreign node in place exactly like a data-rask-managed one.
//
// Two invariants make this safe:
//   * The `raskHeadReconciled` gate keeps the FIRST head morph byte-identical to before, so boot-shell
//     hydration (importmap/base/preload/scoped placeholders) reconciles exactly as it used to.
//   * data-rask-key nodes are NEVER treated as foreign, so the framework's own keyed head nodes — most
//     importantly the scoped-CSS FOUC preload clone (rask-scoped.js), whose __raskHead expando does not
//     survive cloneNode — still reconcile by key instead of duplicating.
//
// Because ownership is marked inline on exactly the nodes derived from the render tree (not by a post-hoc
// sweep of all children), a sibling that a rendered inline <script> injects mid-morph is left unmarked and
// therefore preserved, rather than adopted-as-owned and trimmed on the following render.
let raskHeadReconciled = false;

function morph(from, to) {
    if (from.nodeType !== to.nodeType || from.nodeName !== to.nodeName) {
        _raskReplaceChild(from.parentNode, to, from);
        return;
    }
    if (from.nodeType === 3 || from.nodeType === 8) {
        if (from.nodeValue !== to.nodeValue) from.nodeValue = to.nodeValue;
        return;
    }
    const fa = from.attributes, ta = to.attributes;
    // Reverse walk: removeAttribute mutates the live `fa` NamedNodeMap, so iterate
    // by index from the end to keep the unvisited slots stable.
    for (let i = fa.length - 1; i >= 0; i--) {
        const name = fa[i].name;
        if (!to.hasAttribute(name)) from.removeAttribute(name);
    }
    for (const a of ta) {
        if (from.getAttribute(a.name) !== a.value) from.setAttribute(a.name, a.value);
    }
    const tag = from.tagName;
    if (tag === "INPUT" || tag === "TEXTAREA") {
        // Only inputs with data-rask-on-input stream keystrokes — those need the
        // focus guard so a lagging re-render doesn't clobber mid-typed characters.
        // Change-only inputs (date / number / time / datetime-local / checkbox /
        // radio) commit at change time; the rendered value is canonical and must
        // win, otherwise Chromium leaves a focused date input's dirty value flag
        // stale and the first picker change appears to be dropped.
        const streaming = from.hasAttribute("data-rask-on-input") || to.hasAttribute("data-rask-on-input");
        if (!streaming || document.activeElement !== from) {
            let newVal = to.getAttribute("value");
            if (newVal === null && to.tagName === "TEXTAREA") newVal = to.textContent;
            // No rendered `value` (an <input> with no `value` attribute) means the input is
            // *uncontrolled* — the framework isn't managing its value, so a re-render (including a
            // full-document morph on a full reply — scoped-CSS delivery, reconnect, …) must leave the
            // user's typed DOM value alone rather than reset it to "". A controlled/bound input always
            // renders a `value` attribute (even `value=""`), so it still syncs below.
            // raskShouldSuppressValue runs first so it can clear a confirmed echo even when from.value
            // already equals newVal; a still-pending user edit (incoming !== the committed value) is
            // left untouched.
            if (newVal !== null && !raskShouldSuppressValue(from, newVal) && from.value !== newVal) {
                from.value = newVal;
            }
            // raskShouldSuppressChecked runs first (like the value guard) so a confirmed echo can
            // clear the guard even when from.checked already matches — a lagging frame carrying the
            // pre-click checked is left to the browser's just-applied native state.
            const checked = to.hasAttribute("checked");
            if (!raskShouldSuppressChecked(from, checked) && from.checked !== checked) from.checked = checked;
        }
    }
    // Skip JS-owned elements (marked data-rask-managed) — they're not part of
    // the .NET render tree, so pairing them against the incoming children would
    // either trim them off or replace them with something unrelated. Used by
    // the Server overlay (reconnect spinner sibling of <html>) and the WASM
    // scoped-css / scoped-js bundle tags (head children that don't appear in
    // the .NET-rendered HTML payload).
    // Foreign-head preservation (see the note above raskHeadReconciled): once the head has been
    // reconciled at least once, pull out any head element the morph never produced (a third-party lib
    // injected it since the last render) so it's left in place, exactly like a data-rask-managed node.
    // data-rask-key nodes are NOT foreign — they're framework keyed nodes (e.g. the scoped-CSS FOUC
    // clone) that must reconcile by key rather than duplicate.
    const isHead = from.nodeName === "HEAD";
    const skipForeign = isHead && raskHeadReconciled;
    // Tag a node the morph produces as Rask-owned (head only) so later morphs don't mistake it for a
    // foreign injection. Applied inline to exactly the nodes derived from the render tree.
    const own = isHead ? (n) => { n.__raskHead = true; return n; } : (n) => n;
    const fc = [], tc = [];
    for (let n = from.firstChild; n; n = n.nextSibling) {
        if (n.nodeType === 1 && n.hasAttribute("data-rask-managed")) continue;
        if (skipForeign && n.nodeType === 1 && n.__raskHead !== true && !n.hasAttribute("data-rask-key")) {
            continue;
        }
        fc.push(n);
    }
    for (let m = to.firstChild; m; m = m.nextSibling) tc.push(m);

    // Keyed reconciliation: if any incoming child carries data-rask-key, match
    // by key instead of by position so reordered list items keep their DOM
    // identity (focus, scroll, animations, ::part state) across re-renders.
    // Falls back to the positional walk below when no keys are present.
    let keyed = false;
    for (const node of tc) {
        if (node.nodeType === 1 && node.getAttribute && node.getAttribute("data-rask-key") !== null) {
            keyed = true;
            break;
        }
    }
    if (keyed) {
        const keyMap = new Map();
        const unkeyedFrom = [];
        for (const fn of fc) {
            const fk = (fn.nodeType === 1 && fn.getAttribute) ? fn.getAttribute("data-rask-key") : null;
            if (fk !== null) keyMap.set(fk, fn);
            else unkeyedFrom.push(fn);
        }
        let unkeyedCursor = 0;
        // Sentinel: keep the place we want to insert before. As we move/create
        // keyed nodes we advance this past the just-placed node; unkeyed nodes
        // follow the same anchor.
        let anchor = (fc.length > 0) ? fc[0] : null;
        for (const dst of tc) {
            const dk = (dst.nodeType === 1 && dst.getAttribute) ? dst.getAttribute("data-rask-key") : null;
            let src;
            if (dk !== null) {
                src = keyMap.get(dk) || null;
                if (src) keyMap.delete(dk);
            } else {
                src = unkeyedFrom[unkeyedCursor++] || null;
            }
            if (src === null) {
                _raskInsertBefore(from, own(reviveScript(dst)), anchor);
            } else if (src.nodeType !== dst.nodeType || src.nodeName !== dst.nodeName) {
                _raskInsertBefore(from, own(reviveScript(dst)), anchor);
                // If the from-node we're about to remove IS the anchor, advance the anchor past it
                // first — otherwise the next insert/move would pass a reference node no longer in
                // `from` and insertBefore throws "reference node is not a child". This happens when a
                // keyed sibling promotes the container to keyed reconciliation but some from-side
                // children don't match the new tree by node name (e.g. the SDK-injected <head>
                // importmap / <base> a WASM app hydrates against on a static host).
                if (src === anchor) anchor = anchor.nextSibling;
                _raskRemoveChild(from, src);
            } else {
                if (src !== anchor) _raskMoveBefore(from, src, anchor);
                else anchor = anchor.nextSibling;
                morph(src, dst);
                own(src);
            }
        }
        // Drop any from-side keyed nodes that were not claimed by the new tree.
        keyMap.forEach((n) => {
            if (n.parentNode === from) _raskRemoveChild(from, n);
        });
        // Drop trailing unkeyed nodes too.
        while (unkeyedCursor < unkeyedFrom.length) {
            const leftover = unkeyedFrom[unkeyedCursor++];
            if (leftover.parentNode === from) _raskRemoveChild(from, leftover);
        }
        if (isHead) raskHeadReconciled = true;
        return;
    }

    const max = Math.max(fc.length, tc.length);
    for (let k = 0; k < max; k++) {
        const src = fc[k], dst = tc[k];
        if (!src) _raskAppendChild(from, own(reviveScript(dst)));
        else if (!dst) _raskRemoveChild(from, src);
        else if (src.nodeType !== dst.nodeType || src.nodeName !== dst.nodeName) _raskReplaceChild(from, own(reviveScript(dst)), src);
        else { morph(src, dst); own(src); }
    }
    if (isHead) raskHeadReconciled = true;
}
