// Scoped-JS dispatcher. Concatenated into both rask.js (Server) and rask.wasm.js (WASM)
// at build time via the @@RASK_SCOPED@@ marker — see _RaskBuildClientJs / _RaskSpliceClientJs
// in Rask.Server.csproj and Rask.Wasm.csproj.
//
// Public author surface (sibling `.js` next to a Component):
//   export function rendered(el, firstRender) { /* ... */ }
//   export async function fetchSomething(el, key) { return await x.fetch(key); }
//   // any number of named exports — each becomes a method on the scoped module.
//
// Invocation model: NOT automatic. C# user code calls
//   InvokeJs("name", ...args)             — fire-and-forget
//   InvokeJsAsync<T>("name", ...args)     — await the return value
// from a lifecycle hook (typically OnRendered). The framework ships queued
// invocations in the render payload as `scopedJsInvokes`; the client runtime calls
//   Rask.scoped.invoke(scopeId, method, idOrNull, args)
// for each entry after morph completes. The dispatcher looks up `method` on the
// registered module object, calls it as `module[method](el, ...args)` for the
// first matching `data-rask-mount` element, awaits any returned Promise, and
// — when `idOrNull` is a number — ships the result back via the host-installed
// `Rask.scoped._sendResult(id, value, error)` bridge.
window.Rask = window.Rask || {};
Rask.scoped = (function () {
    var registry = new Map(); // scopeId -> { name: function, ... }

    function register(scopeId, factory) {
        var methods;
        try {
            methods = (typeof factory === 'function') ? factory() : factory;
        } catch (e) {
            console.error('[Rask] scoped-js factory failed for ' + scopeId, e);
            return;
        }
        if (methods && typeof methods === 'object') {
            registry.set(scopeId, methods);
        }
    }

    // host runtime installs this hook to ship the result back across the
    // appropriate transport (WS message on server, JSExport call on WASM).
    function _sendResult(id, value, error) {
        // default no-op — overridden by rask.js / rask.wasm.js
    }

    function _serializeResult(value) {
        if (value === undefined) return null;
        // Keep the wire payload narrow: primitives travel as JSON-native; everything
        // else (objects, arrays, classes) stringifies. C# DeserializeResult<T>
        // handles primitives directly and falls back to the JSON raw text for string T.
        var t = typeof value;
        if (t === 'boolean' || t === 'number' || t === 'string' || value === null) return value;
        try {
            return JSON.stringify(value);
        } catch (e) {
            return String(value);
        }
    }

    function invoke(scopeId, method, id, args) {
        if (!scopeId || !method) return;
        var hasId = (typeof id === 'number');
        var methods = registry.get(scopeId);
        if (!methods) {
            if (hasId) _sendResult(id, null, null);
            return;
        }
        var fn = methods[method];
        if (typeof fn !== 'function') {
            if (hasId) _sendResult(id, null, null);
            return;
        }
        var extra = Array.isArray(args) ? args : [];
        // For fire-and-forget invocations, dispatch against EVERY matching element.
        // For await-the-result invocations (id present), only the first matching
        // element's return value is shipped back — matches Component.InvokeJsAsync's
        // documented contract.
        var nodes = document.querySelectorAll('[data-rask-mount="' + scopeId + '"]');
        if (hasId) {
            var node = nodes[0];
            if (!node) {
                _sendResult(id, null, null);
                return;
            }
            try {
                var result = fn.apply(null, [node].concat(extra));
                if (result && typeof result.then === 'function') {
                    result.then(
                        function (v) {
                            _sendResult(id, _serializeResult(v), null);
                        },
                        function (err) {
                            _sendResult(id, null, (err && err.message) || String(err));
                        }
                    );
                } else {
                    _sendResult(id, _serializeResult(result), null);
                }
            } catch (e) {
                _sendResult(id, null, e && e.message || String(e));
            }
            return;
        }
        for (var i = 0; i < nodes.length; i++) {
            var n = nodes[i];
            try {
                fn.apply(null, [n].concat(extra));
            } catch (e) {
                console.error('[Rask] ' + method + ' failed for ' + scopeId, e);
            }
        }
    }

    return {
        register: register,
        invoke: invoke,
        // The host runtime patches this with a transport-specific sender.
        set _sendResult(fn) {
            _sendResult = fn;
        },
        get _sendResult() {
            return _sendResult;
        }
    };
})();
