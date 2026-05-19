// Scoped-JS dispatcher. Concatenated into both rask.js (Server) and rask.wasm.js (WASM)
// at build time via the @@RASK_SCOPED@@ marker — see _RaskBuildClientJs / _RaskSpliceClientJs
// in Rask.Server.csproj and Rask.Wasm.csproj.
//
// Public author surface (sibling `.js` next to a Component):
//   export function rendered(el) {
//       // do something with el; return a cleanup function if you need teardown.
//       return () => { /* cleanup runs before el leaves the DOM */ };
//   }
//
// The bundle delivered from the server / inlined on WASM consists of per-component
// `Rask.scoped.register(scopeId, factory)` calls (one per .js sibling). The morph
// algorithm in rask-morph.js calls `Rask.scoped.walkRendered` / `Rask.scoped.walkRemoved`
// against every inserted / removed subtree; the server runtime walks the initial DOM
// once after WS open, the WASM runtime walks once after setExports.
window.Rask = window.Rask || {};
Rask.scoped = (function () {
    var registry = new Map();    // scopeId -> rendered fn
    var cleanups = new WeakMap(); // element -> cleanup fn returned by rendered()

    function register(scopeId, factory) {
        var hook;
        try {
            hook = (typeof factory === 'function') ? factory() : factory;
        } catch (e) {
            console.error('[Rask] scoped-js factory failed for ' + scopeId, e);
            return;
        }
        if (typeof hook === 'function') {
            registry.set(scopeId, hook);
        }
    }

    function dispatch(node, phase) {
        if (!node || node.nodeType !== 1) return;
        var scopeId = node.getAttribute && node.getAttribute('data-rask-mount');
        if (!scopeId) return;
        if (phase === 'rendered') {
            var fn = registry.get(scopeId);
            if (typeof fn !== 'function') return;
            // Each render re-runs the hook (mirrors Blazor's OnAfterRender semantics + the
            // React useEffect-with-no-deps shape). Tear down the previous render's effect
            // before kicking off the new one so listeners / timers don't accumulate.
            var prevCleanup = cleanups.get(node);
            if (prevCleanup) {
                cleanups.delete(node);
                try { prevCleanup(node); }
                catch (e) { console.error('[Rask] cleanup failed for ' + scopeId, e); }
            }
            try {
                var result = fn(node);
                if (typeof result === 'function') {
                    cleanups.set(node, result);
                }
            } catch (e) {
                console.error('[Rask] rendered failed for ' + scopeId, e);
            }
        } else {
            // Element is leaving the DOM. Only fire cleanup; no rendered to invoke.
            var c = cleanups.get(node);
            if (!c) return;
            cleanups.delete(node);
            try { c(node); }
            catch (e) { console.error('[Rask] cleanup failed for ' + scopeId, e); }
        }
    }

    function walk(root, phase) {
        if (!root) return;
        if (root.nodeType === 1 && root.hasAttribute && root.hasAttribute('data-rask-mount')) {
            dispatch(root, phase);
        }
        if (root.querySelectorAll) {
            var nodes = root.querySelectorAll('[data-rask-mount]');
            for (var i = 0; i < nodes.length; i++) dispatch(nodes[i], phase);
        }
    }

    return {
        register: register,
        dispatch: dispatch,
        walkRendered: function (r) { walk(r, 'rendered'); },
        walkRemoved: function (r) { walk(r, 'removed'); }
    };
})();
