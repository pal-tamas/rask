// Shared client-side morph algorithm consumed by both rask.js (Server) and
// rask.wasm.js (WASM). Concatenated into each runtime at build time — see the
// MSBuild "_RaskBuildClientJs" target in Rask.Server.csproj and Rask.Wasm.csproj.
//
// Why concat instead of import / network split:
//  - rask.js is a classic <script> served from /rask/rask.js (no ES-module hook).
//  - rask.wasm.js is loaded by JSHost.ImportAsync as an ES module.
// Concat sidesteps the loader mismatch and keeps the single-file delivery model.

// Scripts produced by DOMParser have their "already started" flag set, so the
// browser silently skips them when morph() appends them into the live document.
// Rebuild script nodes via createElement so they actually execute, propagate
// every attribute (type=module, defer, integrity, nonce, crossorigin, …), and
// fire raskAfterMorph again once external scripts finish loading — inline
// scripts run synchronously on insertion and may early-return if they depend
// on a not-yet-loaded global like window.hljs.
function reviveScript(node) {
    if (!node || node.nodeType !== 1 || node.tagName !== "SCRIPT") return node;
    var s = document.createElement("script");
    for (var i = 0; i < node.attributes.length; i++) {
        var a = node.attributes[i];
        s.setAttribute(a.name, a.value);
    }
    if (s.src) {
        s.async = false;
        s.addEventListener("load", function () {
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

function _raskAppendChild(parent, dst) {
    parent.appendChild(dst);
}

function _raskRemoveChild(parent, src) {
    parent.removeChild(src);
}

function _raskReplaceChild(parent, dst, src) {
    parent.replaceChild(dst, src);
}

function morph(from, to) {
    if (from.nodeType !== to.nodeType || from.nodeName !== to.nodeName) {
        _raskReplaceChild(from.parentNode, to, from);
        return;
    }
    if (from.nodeType === 3 || from.nodeType === 8) {
        if (from.nodeValue !== to.nodeValue) from.nodeValue = to.nodeValue;
        return;
    }
    var fa = from.attributes, ta = to.attributes;
    for (var i = fa.length - 1; i >= 0; i--) {
        var name = fa[i].name;
        if (!to.hasAttribute(name)) from.removeAttribute(name);
    }
    for (var j = 0; j < ta.length; j++) {
        var a = ta[j];
        if (from.getAttribute(a.name) !== a.value) from.setAttribute(a.name, a.value);
    }
    var tag = from.tagName;
    if (tag === "INPUT" || tag === "TEXTAREA") {
        // Only inputs with data-rask-on-input stream keystrokes — those need the
        // focus guard so a lagging re-render doesn't clobber mid-typed characters.
        // Change-only inputs (date / number / time / datetime-local / checkbox /
        // radio) commit at change time; the rendered value is canonical and must
        // win, otherwise Chromium leaves a focused date input's dirty value flag
        // stale and the first picker change appears to be dropped.
        var streaming = from.hasAttribute("data-rask-on-input") || to.hasAttribute("data-rask-on-input");
        if (!streaming || document.activeElement !== from) {
            var newVal = to.getAttribute("value");
            if (newVal === null && to.tagName === "TEXTAREA") newVal = to.textContent;
            if (newVal === null) newVal = "";
            if (from.value !== newVal) from.value = newVal;
            var checked = to.hasAttribute("checked");
            if (from.checked !== checked) from.checked = checked;
        }
    }
    // Skip JS-owned elements (marked data-rask-managed) — they're not part of
    // the .NET render tree, so pairing them against the incoming children would
    // either trim them off or replace them with something unrelated. Used by
    // the Server overlay (reconnect spinner sibling of <html>) and the WASM
    // scoped-css / scoped-js bundle tags (head children that don't appear in
    // the .NET-rendered HTML payload).
    var fc = [], tc = [];
    for (var n = from.firstChild; n; n = n.nextSibling) {
        if (n.nodeType === 1 && n.hasAttribute("data-rask-managed")) continue;
        fc.push(n);
    }
    for (var m = to.firstChild; m; m = m.nextSibling) tc.push(m);

    // Keyed reconciliation: if any incoming child carries data-rask-key, match
    // by key instead of by position so reordered list items keep their DOM
    // identity (focus, scroll, animations, ::part state) across re-renders.
    // Falls back to the positional walk below when no keys are present.
    var keyed = false;
    for (var ki = 0; ki < tc.length; ki++) {
        if (tc[ki].nodeType === 1 && tc[ki].getAttribute && tc[ki].getAttribute("data-rask-key") !== null) {
            keyed = true;
            break;
        }
    }
    if (keyed) {
        var keyMap = new Map();
        var unkeyedFrom = [];
        for (var fi = 0; fi < fc.length; fi++) {
            var fn = fc[fi];
            var fk = (fn.nodeType === 1 && fn.getAttribute) ? fn.getAttribute("data-rask-key") : null;
            if (fk !== null) keyMap.set(fk, fn);
            else unkeyedFrom.push(fn);
        }
        var unkeyedCursor = 0;
        // Sentinel: keep the place we want to insert before. As we move/create
        // keyed nodes we advance this past the just-placed node; unkeyed nodes
        // follow the same anchor.
        var anchor = (fc.length > 0) ? fc[0] : null;
        for (var ti = 0; ti < tc.length; ti++) {
            var dst = tc[ti];
            var dk = (dst.nodeType === 1 && dst.getAttribute) ? dst.getAttribute("data-rask-key") : null;
            var src;
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
                _raskRemoveChild(from, src);
            } else {
                if (src !== anchor) from.insertBefore(src, anchor);
                else anchor = anchor.nextSibling;
                morph(src, dst);
            }
        }
        // Drop any from-side keyed nodes that were not claimed by the new tree.
        keyMap.forEach(function (n) {
            if (n.parentNode === from) _raskRemoveChild(from, n);
        });
        // Drop trailing unkeyed nodes too.
        while (unkeyedCursor < unkeyedFrom.length) {
            var leftover = unkeyedFrom[unkeyedCursor++];
            if (leftover.parentNode === from) _raskRemoveChild(from, leftover);
        }
        return;
    }

    var max = Math.max(fc.length, tc.length);
    for (var k = 0; k < max; k++) {
        var src = fc[k], dst = tc[k];
        if (!src) _raskAppendChild(from, reviveScript(dst));
        else if (!dst) _raskRemoveChild(from, src);
        else if (src.nodeType !== dst.nodeType || src.nodeName !== dst.nodeName) _raskReplaceChild(from, reviveScript(dst), src);
        else morph(src, dst);
    }
}
