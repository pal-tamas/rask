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

// User-edit provenance for the redeploy restore. When a replacement server can't carry a session over,
// the page reloads, and the Server runtime re-applies the fields the user had actually edited (see
// saveRestoreFields / applyRestoreFields in rask.js). It re-applies one only when the REPLACEMENT
// server rendered the same base the old one did — which is what makes that a three-way merge (base /
// the user's edit / what the new server rendered) rather than a guess about whose value is newer.
//
// The base has to be captured the FIRST time a field goes dirty. It cannot be read off the DOM later:
// every echo of the user's own keystrokes rewrites the `value` ATTRIBUTE, because morph() and the diff
// both sync attributes unconditionally and guard only the `.value` PROPERTY. By reload time the
// attribute IS the user's text, base would equal ours, and every edit would compare unequal against a
// pristine replacement and be dropped — the feature would be a silent no-op. Hence capture-once: a
// second edit to the same field must not overwrite the base the first one recorded.
//
// Same WeakMap-backed-global shape as the two guards above, and for the same reason: reachable from the
// spliced morph, from rask-input.js and from the host runtime regardless of splice ordering.
function _raskDirtyFields() {
    return window.__raskDirtyFields || (window.__raskDirtyFields = new WeakMap());
}

function raskNoteDirtyField(el) {
    if (!el) return;
    const map = _raskDirtyFields();
    if (map.has(el)) return;                        // capture-once — the first edit owns the base
    map.set(el, raskFieldBase(el));
}

function raskIsDirtyField(el) {
    return !!el && _raskDirtyFields().has(el);
}

function raskDirtyFieldBase(el) {
    const map = _raskDirtyFields();
    return el && map.has(el) ? map.get(el) : undefined;
}

// What the server rendered for a control, read from ATTRIBUTES only — never from a property. The two
// disagree in ways that would silently corrupt the comparison: a type=number/date input sanitizes an
// unparseable attribute to "" on `.value`, and a textarea's `.value` normalizes the CRLF its text
// content keeps. Bases are only ever compared to other bases, so attribute-vs-attribute is the rule.
//
// `null` is preserved and is NOT the same as "": it means the server rendered no `value` attribute at
// all — an uncontrolled input, which morph deliberately never writes to (see the uncontrolled rule in
// morph() below). The restore treats the two differently, so they must not be flattened together.
function raskFieldBase(el) {
    if (el.tagName === "TEXTAREA") return el.textContent;
    const type = (el.getAttribute("type") || "").toLowerCase();
    if (type === "checkbox") return el.hasAttribute("checked");
    if (type === "radio") return raskRadioGroupBase(el);
    return el.getAttribute("value");
}

// A radio's meaningful state is its GROUP's, not its own: "which value is selected" rather than "is
// this one checked". Per-element `checked` would let a restore re-check the user's pick while the
// replacement server had moved the selection elsewhere — and natively un-check the server's choice —
// without the bases ever comparing unequal. So the base is the value of whichever member carries the
// `checked` attribute, or "" for a group the server rendered with nothing selected.
//
// The group is same-name AND same form owner; two forms on one page can each have a `Plan` group.
function raskRadioGroupBase(el) {
    const group = raskRadioGroup(el);
    for (const r of group) {
        if (r.hasAttribute("checked")) return r.getAttribute("value") || "";
    }

    return "";
}

function raskRadioGroup(el) {
    const name = el.getAttribute("name");
    if (!name) return [el];
    const out = [];
    // Matched by attribute rather than a name selector so this stays free of CSS.escape, which the
    // shared modules can't assume — a name is app-authored and may hold selector metacharacters.
    for (const r of document.querySelectorAll("input[type=radio]")) {
        if (r.getAttribute("name") === name && r.form === el.form) out.push(r);
    }

    return out.length > 0 ? out : [el];
}

// Third-party <head> preservation. Libraries routinely inject <style>/<link>/<script> into <head> at
// runtime (a code editor's theme colours, a charting lib, a syntax highlighter, analytics). Those nodes
// aren't in the .NET-rendered head, so the reconciler below would trim them on the next head morph. Rather
// than change the reconciliation (its invariants — keyed FOUC clones, boot-shell hydration, self-healing —
// are load-bearing), we watch <head> and tag anything a library injects with data-rask-managed, which the
// reconciler ALREADY skips (see the fc-building loop). The framework's own head mutations happen during an
// apply (a head morph, or an applyDiff InsertSubtree of a Head-declared script/link); those are discarded
// from the observer queue so they're never mistaken for foreign. data-rask-key nodes (the framework's keyed
// head links, incl. the scoped-CSS FOUC preload clone) are never tagged — they must reconcile by key.
let _raskHeadObserver = null;
let _raskObservedHead = null;

function _raskEnsureHeadObserver() {
    if (typeof MutationObserver === "undefined" || typeof document === "undefined" || !document.head) {
        return;
    }
    // Already watching the live <head> — nothing to do.
    if (_raskHeadObserver && _raskObservedHead === document.head) {
        return;
    }
    // First install, or the <head> element was replaced (not morphed in place) — (re)arm on the live head.
    if (_raskHeadObserver) _raskHeadObserver.disconnect();
    _raskObservedHead = document.head;
    // The callback receives the pending records as its argument — do NOT call takeRecords() here (it would
    // return empty, since delivery already drained them). takeRecords() is only for the synchronous flush
    // at a head morph / applyDiff, where the records are still pending.
    _raskHeadObserver = new MutationObserver((records) => _raskTagHeadRecords(records));
    _raskHeadObserver.observe(_raskObservedHead, { childList: true });
}

// Tag the nodes added by these mutation records — a <style>/<link>/<script> a library injected — with
// data-rask-managed so the reconciler's skip preserves them. Never tags data-rask-key nodes (the
// framework's own keyed head links, e.g. the scoped-CSS FOUC clone, which must reconcile by key).
function _raskTagHeadRecords(records) {
    for (const r of records) {
        for (const n of r.addedNodes) {
            if (n.nodeType === 1 && !n.hasAttribute("data-rask-key") && !n.hasAttribute("data-rask-managed")) {
                n.setAttribute("data-rask-managed", "");
            }
        }
    }
}

// Synchronous flush at the start of a head morph: tag foreign nodes injected since the last drain that the
// async observer callback hasn't processed yet, so this morph preserves them.
function _raskTagForeignHeadNodes() {
    if (_raskHeadObserver) _raskTagHeadRecords(_raskHeadObserver.takeRecords());
}

// Drop the head mutations the framework itself just made (during a morph or applyDiff) so the async
// observer never tags framework-inserted head nodes as foreign. Called at the end of every head morph and
// at the end of applyDiff (rask-dom.js).
function _raskDiscardFrameworkHeadMutations() {
    if (_raskHeadObserver) _raskHeadObserver.takeRecords();
}

// Install eagerly when the client bundle loads, so a library that injects into <head> before the first
// head morph is still observed (the lazy install inside morph() is the fallback for when document.head
// isn't ready at load time). The observer only tags nodes ADDED after it arms — the boot-shell head is
// left alone.
_raskEnsureHeadObserver();

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
    // Reconciling the live <head>: before pairing children, tag anything a third-party library injected
    // (see the note above _raskHeadObserver) as data-rask-managed so the skip below preserves it. The
    // observer is installed lazily on the first head morph — library injections happen after boot.
    const isDocHead = typeof document !== "undefined" && from === document.head;
    if (isDocHead) {
        _raskEnsureHeadObserver();
        _raskTagForeignHeadNodes();
    }
    // Skip JS-owned elements (marked data-rask-managed) — they're not part of
    // the .NET render tree, so pairing them against the incoming children would
    // either trim them off or replace them with something unrelated. Used by
    // the Server overlay (reconnect spinner sibling of <html>) and the WASM
    // scoped-css / scoped-js bundle tags (head children that don't appear in
    // the .NET-rendered HTML payload).
    //
    // The filter is symmetric: an incoming (to-side) child carrying the marker is
    // always a misuse — a .NET-rendered node is by definition part of the payload,
    // so the marker contradicts itself. Skipping it makes that mistake a harmless
    // no-op; without this, the from-side node is filtered out but the to-side one
    // isn't, so every morph appends a fresh unpaired copy (unbounded DOM growth).
    const fc = [], tc = [];
    for (let n = from.firstChild; n; n = n.nextSibling) {
        if (n.nodeType === 1 && n.hasAttribute("data-rask-managed")) continue;
        fc.push(n);
    }
    for (let m = to.firstChild; m; m = m.nextSibling) {
        if (m.nodeType === 1 && m.hasAttribute("data-rask-managed")) continue;
        tc.push(m);
    }

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
                _raskInsertBefore(from, reviveScript(dst), anchor);
            } else if (src.nodeType !== dst.nodeType || src.nodeName !== dst.nodeName) {
                _raskInsertBefore(from, reviveScript(dst), anchor);
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
        if (isDocHead) _raskDiscardFrameworkHeadMutations();
        return;
    }

    const max = Math.max(fc.length, tc.length);
    for (let k = 0; k < max; k++) {
        const src = fc[k], dst = tc[k];
        if (!src) _raskAppendChild(from, reviveScript(dst));
        else if (!dst) _raskRemoveChild(from, src);
        else if (src.nodeType !== dst.nodeType || src.nodeName !== dst.nodeName) _raskReplaceChild(from, reviveScript(dst), src);
        else morph(src, dst);
    }
    if (isDocHead) _raskDiscardFrameworkHeadMutations();
}
