(function () {
    "use strict";

    var root = document.querySelector("[data-rask-root]");
    if (!root) return;

    var sessionId = root.getAttribute("data-rask-root");
    var proto = location.protocol === "https:" ? "wss:" : "ws:";
    var wsUrl = proto + "//" + location.host + "/rask/ws";

    var ws = null;
    var queue = [];
    var open = false;
    var attempt = 0;
    var reconnectTimer = null;
    var suppressEvents = false;
    var overlay = installOverlay();

    connect();

    function connect() {
        ws = new WebSocket(wsUrl);

        ws.addEventListener("open", function () {
            open = true;
            attempt = 0;
            suppressEvents = false;
            ws.send(JSON.stringify({type: "hello", session: sessionId}));
            for (var i = 0; i < queue.length; i++) ws.send(queue[i]);
            queue.length = 0;
            hideOverlay();
        });

        ws.addEventListener("message", function (e) {
            var data;
            try {
                data = JSON.parse(e.data);
            } catch (err) {
                return;
            }
            if (data.type === "session" && data.status === "unknown") {
                location.reload();
                return;
            }
            // Diff-mode payload (kind:"diff"): apply ops directly against the live
            // DOM instead of morphing a fresh document. Wire format matches
            // LivePayload.BuildPayloadUtf8Diff (C# side). Each op carries a Path
            // (sequence of childNodes indices from the document root) and an op-
            // specific payload. Falls through to history/auth/download handling
            // afterwards so navigation + side effects still flow.
            if (data.kind === "diff" && Array.isArray(data.ops)) {
                applyDiff(data.ops, Array.isArray(data.names) ? data.names : null);
                if (data.history && typeof data.history.url === "string") {
                    if (data.history.action === "replace") {
                        history.replaceState({rask: true}, "", data.history.url);
                    } else {
                        history.pushState({rask: true}, "", data.history.url);
                    }
                }
                if (typeof window.raskAfterMorph === "function") window.raskAfterMorph();
                return;
            }
            var freshHtml = null;
            if (typeof data.html === "string") {
                var doc = new DOMParser().parseFromString(data.html, "text/html");
                // Morph the whole <html> element so head changes (title, per-page Head
                // asset contributions, scoped-css/scoped-js hash bumps) propagate too.
                // Server now sends the full document via BuildPayloadUtf8WithRoot —
                // matching the WASM runtime's morph target.
                freshHtml = doc.documentElement;
            }
            // All post-morph work (history push, scoped CSS/JS apply, scoped-JS
            // dispatch, raskAfterMorph hook) runs inside the applyDom callback so
            // dispatch reads the freshly-morphed DOM rather than the pre-morph one.
            function applyDom() {
                if (freshHtml) {
                    morph(document.documentElement, freshHtml);
                    root = document.querySelector("[data-rask-root]") || root;
                    // Pick up any newly-inserted Head-declared external assets
                    // (e.g., a page-specific Script in Component.Head) so their
                    // load events feed into the Rask.* invoke gate.
                    scanHeadAssets();
                }
                if (data.history && typeof data.history.url === "string") {
                    if (data.history.action === "replace") {
                        history.replaceState({rask: true}, "", data.history.url);
                    } else {
                        history.pushState({rask: true}, "", data.history.url);
                    }
                }
                if (typeof data.cssHash === "string") applyScopedCss(data.cssHash);
                if (typeof data.jsHash === "string") applyScopedJs(data.jsHash);
                // IJSRuntime.InvokeAsync<T> dispatch: each entry resolves a dotted
                // identifier on `window` (e.g. `sessionStorage.getItem` or
                // `Rask.{TypeName}.{method}`), invokes it with the JSON-decoded args,
                // and ships the result back as a jsResult message keyed by taskId.
                // Rask.*-prefixed identifiers are deferred via dispatchJsInvoke's
                // pendingScopedInvokes queue until the scoped-JS bundle has loaded.
                if (Array.isArray(data.jsInvokes)) {
                    for (var ji = 0; ji < data.jsInvokes.length; ji++) {
                        dispatchJsInvoke(data.jsInvokes[ji]);
                    }
                }
                // dotNetResult: reply to a JS-initiated DotNet.invokeMethodAsync call.
                // Routed by the DotNet shim's pending-call table to resolve/reject
                // the matching JS Promise.
                if (data.type === "dotNetResult" && typeof data.callId === "string") {
                    window.DotNet._endInvokeDotNet(data);
                }
                if (typeof window.raskAfterMorph === "function") window.raskAfterMorph();
            }

            applyDom();
            // Out-of-band frames carry no html — process the supplemental fields and
            // bail before any morph-related work runs.
            if (data.type === "dotNetResult" && typeof data.callId === "string"
                && typeof data.html !== "string") {
                window.DotNet._endInvokeDotNet(data);
                return;
            }
            if (data.auth && typeof data.auth.ticket === "string") {
                redeemAuthTicket(data.auth);
            }
            if (data.download && typeof data.download.url === "string"
                && typeof data.download.filename === "string") {
                triggerDownload(data.download.url, data.download.filename);
            }
        });

        ws.addEventListener("close", scheduleReconnect);
        ws.addEventListener("error", scheduleReconnect);
    }

    function scheduleReconnect() {
        if (reconnectTimer !== null) return;
        open = false;
        showOverlay();
        var delays = [500, 1000, 2000, 4000, 5000];
        var delay = delays[Math.min(attempt, delays.length - 1)];
        attempt++;
        reconnectTimer = setTimeout(function () {
            reconnectTimer = null;
            connect();
        }, delay);
    }

    function installOverlay() {
        // data-rask-managed tells rask-morph.js's diff to treat this node as
        // invisible — these are framework-managed siblings of the server-rendered
        // tree and would otherwise get trimmed on the first morph that doesn't
        // include them. data-rask-overlay is just a query selector tag.
        var style = document.createElement("style");
        style.setAttribute("data-rask-overlay", "");
        style.setAttribute("data-rask-managed", "");
        style.textContent =
            ".rask-overlay{position:fixed;inset:0;background:rgba(20,20,20,.45);" +
            "display:none;align-items:center;justify-content:center;z-index:2147483647;" +
            "font:14px/1.4 system-ui,-apple-system,Segoe UI,sans-serif;color:#fff;" +
            "backdrop-filter:blur(2px);-webkit-backdrop-filter:blur(2px);}" +
            ".rask-overlay[data-show]{display:flex;}" +
            ".rask-overlay__card{background:rgba(0,0,0,.7);padding:18px 22px;border-radius:8px;" +
            "display:flex;align-items:center;gap:12px;box-shadow:0 8px 24px rgba(0,0,0,.3);}" +
            ".rask-overlay__spinner{width:16px;height:16px;border:2px solid rgba(255,255,255,.3);" +
            "border-top-color:#fff;border-radius:50%;animation:rask-spin .8s linear infinite;}" +
            "@keyframes rask-spin{to{transform:rotate(360deg);}}";
        document.head.appendChild(style);

        var el = document.createElement("div");
        el.className = "rask-overlay";
        el.setAttribute("data-rask-managed", "");
        el.setAttribute("aria-live", "polite");
        el.setAttribute("aria-hidden", "true");
        el.innerHTML =
            '<div class="rask-overlay__card">' +
            '<span class="rask-overlay__spinner" aria-hidden="true"></span>' +
            '<span>Reconnecting…</span>' +
            '</div>';
        document.documentElement.appendChild(el);
        return el;
    }

    function showOverlay() {
        overlay.setAttribute("data-show", "");
        overlay.setAttribute("aria-hidden", "false");
        if ("inert" in document.body) document.body.inert = true;
    }

    function hideOverlay() {
        overlay.removeAttribute("data-show");
        overlay.setAttribute("aria-hidden", "true");
        if ("inert" in document.body) document.body.inert = false;
    }

    function applyScopedCss(hash) {
        var url = "/_rask/scoped.css?v=" + hash;
        var link = document.querySelector("link[data-rask-scoped]");
        if (link) {
            if (link.getAttribute("href") !== url) link.setAttribute("href", url);
            return;
        }
        link = document.createElement("link");
        link.rel = "stylesheet";
        link.setAttribute("data-rask-scoped", "");
        link.href = url;
        document.head.appendChild(link);
    }

    var scopedJsReady = false;
    var pendingScopedInvokes = [];

    // External Head-declared <script src> and <link rel=stylesheet> are tracked
    // here so Rask.* JS invokes can wait until each declared dep has reached a
    // terminal state — load, error, OR a 5-second safety timeout — before
    // firing. Without this, a component invoking e.g. window.hljs in its
    // OnRenderedAsync would have to hand-roll its own load-event workaround
    // (CodeSample.js used to do exactly that). The gate is global on purpose:
    // components don't know about each other's deps, and the alternative —
    // per-invoke dependency declarations — pushes API surface back onto users.
    //
    // CONTRACT: the gate guarantees the asset's terminal event has fired
    // before draining queued Rask.* invokes — NOT that the asset loaded
    // successfully. A failed asset (CDN flake, refresh cache miss, extension
    // block, integrity mismatch, CSP) still terminates the gate via its
    // 'error' event or the 5s timeout, and queued invokes run anyway. User
    // JS that depends on a global the asset was meant to define MUST be
    // defensive — e.g. `if (typeof window.hljs === "undefined") return;`.
    // The framework logs a clear warning on the failure paths so the
    // resulting TypeError isn't a mystery.
    var pendingHeadAssets = new Set();
    var trackedHeadAssets = new WeakSet();
    var failedHeadAssets = new Set();
    var HEAD_ASSET_LOAD_TIMEOUT_MS = 5000;

    function isAssetAlreadyLoaded(url) {
        if (!url || !window.performance || !performance.getEntriesByName) return false;
        var entries = performance.getEntriesByName(url);
        for (var i = 0; i < entries.length; i++) {
            if (entries[i].responseEnd > 0) return true;
        }
        return false;
    }

    function trackHeadAsset(el) {
        if (!el || el.nodeType !== 1 || trackedHeadAssets.has(el)) return;
        // The scoped tags have their own gate (scopedJsReady / applyScopedCss
        // bookkeeping); don't double-track.
        if (el.hasAttribute("data-rask-scoped") || el.hasAttribute("data-rask-scoped-js")) return;
        var url;
        if (el.tagName === "SCRIPT" && el.src) url = el.src;
        else if (el.tagName === "LINK" && el.rel === "stylesheet" && el.href) url = el.href;
        else return;
        trackedHeadAssets.add(el);
        if (isAssetAlreadyLoaded(url)) return;
        pendingHeadAssets.add(el);
        var finish = function (outcome) {
            if (!pendingHeadAssets.delete(el)) return;
            if (outcome === "error" || outcome === "timeout") {
                failedHeadAssets.add(url);
                var reason = outcome === "error"
                    ? "fired 'error' event (network failure / blocked / integrity mismatch / CSP)"
                    : "did not fire load/error within " + HEAD_ASSET_LOAD_TIMEOUT_MS + "ms — proceeding anyway";
                // console.warn rather than .error: the page CAN still render
                // (the user's defensive code is the contract). Surface enough
                // context that the consequent TypeError in user JS is traceable
                // back to the asset that failed.
                console.warn("[Rask] Head asset (" + el.tagName.toLowerCase() + ") " + url + " " + reason + ". " +
                    "Queued Rask.* invokes will run; user JS depending on this asset's global must be defensive.");
            }
            maybeDrainPendingInvokes();
        };
        el.addEventListener("load", function () { finish("load"); }, {once: true});
        el.addEventListener("error", function () { finish("error"); }, {once: true});
        // Safety: the load/error event may have fired between insertion and
        // our listener attach (cache hit on a CDN). Performance.getEntriesByName
        // covers the common case; the timeout covers everything else so a
        // missed event doesn't hold Rask.* invokes forever.
        setTimeout(function () { finish("timeout"); }, HEAD_ASSET_LOAD_TIMEOUT_MS);
    }

    function scanHeadAssets() {
        var els = document.head.querySelectorAll("script[src], link[rel=stylesheet]");
        for (var i = 0; i < els.length; i++) trackHeadAsset(els[i]);
    }

    function headAssetsReady() {
        return pendingHeadAssets.size === 0;
    }

    function maybeDrainPendingInvokes() {
        if (!scopedJsReady || !headAssetsReady()) return;
        var pending = pendingScopedInvokes;
        pendingScopedInvokes = [];
        for (var i = 0; i < pending.length; i++) {
            dispatchJsInvoke(pending[i]);
        }
    }

    function markScopedJsReady() {
        if (scopedJsReady) return;
        scopedJsReady = true;
        maybeDrainPendingInvokes();
    }

    function attachScopedJsLoadListener(scriptEl) {
        // `defer` scripts emitted in the initial HTML have typically already
        // executed by the time rask.js runs — there's no future "load" event
        // to wait for. Detect that state via the `Rask.{TypeName}` IIFE having
        // populated `window.Rask` at least once. Falls back to a load listener
        // for the rarer case where the script tag has been inserted (or
        // re-inserted by applyScopedJs) and hasn't fired its load event yet.
        if (window.Rask && Object.keys(window.Rask).length > 0) {
            markScopedJsReady();
            return;
        }
        scriptEl.addEventListener("load", markScopedJsReady, {once: true});
        // Edge: the IIFE assigns `window.Rask.{TypeName}` synchronously, but
        // the load event hasn't fired yet (e.g. during the deferred-script
        // execution gap). Poll briefly to catch this — once the global is
        // populated we're ready regardless of whether `load` fired.
        var attempts = 0;
        var poll = setInterval(function () {
            attempts++;
            if (window.Rask && Object.keys(window.Rask).length > 0) {
                clearInterval(poll);
                markScopedJsReady();
            } else if (attempts > 200) {
                // Give up after ~10s — bundle likely failed to load; the load
                // listener above is still active for late successes.
                clearInterval(poll);
            }
        }, 50);
    }

    // The initial HTML carries a `data-rask-scoped-js` tag rendered by the
    // server's first GET. Wire up the readiness signal for it on boot so the
    // first WS frame's pending Rask.* invokes can fire as soon as the IIFE
    // executes. Without this, `applyScopedJs(hash)` later early-returns
    // because the URL already matches the existing tag, and `scopedJsReady`
    // never flips to true — every Rask.* invoke queues forever.
    (function bootstrapScopedJsReady() {
        var existing = document.querySelector("script[data-rask-scoped-js]");
        if (!existing) return;
        attachScopedJsLoadListener(existing);
    })();

    // Initial sweep for Head-declared external assets emitted by the server's
    // first GET. New assets added by morph (e.g., a page-specific Head script)
    // are picked up in applyDom() after the morph completes.
    scanHeadAssets();

    function applyScopedJs(hash) {
        var url = "/_rask/scoped.js?v=" + hash;
        var existing = document.querySelector("script[data-rask-scoped-js]");
        if (existing && existing.getAttribute("src") === url) {
            // Same bundle already in the page — bootstrapScopedJsReady (or a
            // prior applyScopedJs call) already hooked up the readiness
            // signal. Nothing to do.
            return;
        }
        // Replace rather than mutate src — browsers don't re-evaluate a <script>
        // on src change. The newly inserted script's IIFEs assign each module to
        // `window.Rask.{TypeName}`; until it executes those globals don't exist
        // and identifier resolution for any `Rask.X.Y` call will throw "Could not
        // find 'Rask.X.Y' on target", which faults the awaiting InvokeAsync<T>.
        // Defer Rask.* invokes until the script has loaded, then drain them.
        scopedJsReady = false;
        var script = document.createElement("script");
        script.setAttribute("data-rask-scoped-js", "");
        script.defer = true;
        script.src = url;
        script.addEventListener("load", markScopedJsReady, {once: true});
        if (existing && existing.parentNode) existing.parentNode.removeChild(existing);
        document.head.appendChild(script);
    }

    function send(payload) {
        if (suppressEvents) return;
        var msg = JSON.stringify(payload);
        if (open && ws && ws.readyState === WebSocket.OPEN) ws.send(msg);
        else queue.push(msg);
    }

    function redeemAuthTicket(auth) {
        suppressEvents = true;
        fetch("/_rask/auth/redeem", {
            method: "POST",
            headers: {"content-type": "application/json"},
            body: JSON.stringify({ticket: auth.ticket, session: sessionId}),
            credentials: "same-origin"
        }).then(function () {
            try {
                if (ws) ws.close(1000, "auth-refresh");
            } catch (e) {
            }
        }).catch(function (err) {
            try {
                console.error("Rask auth redeem failed:", err);
            } catch (e) {
            }
            // Fallback: a full reload picks up whatever cookie state is current.
            location.reload();
        });
    }

    function inRoot(el) {
        return root.contains(el);
    }

    document.addEventListener("click", function (e) {
        if (e.defaultPrevented) return;
        if (e.button !== 0 || e.metaKey || e.ctrlKey || e.shiftKey || e.altKey) return;
        var a = e.target.closest("a[data-rask-nav]");
        if (!a) return;
        if (a.getAttribute("target") === "_blank") return;
        var href = a.getAttribute("href");
        if (!href) return;
        var url;
        try {
            url = new URL(href, location.href);
        } catch (err) {
            return;
        }
        if (url.origin !== location.origin) return;
        e.preventDefault();
        flushInputsNow();
        send({type: "navigate", path: url.pathname, query: url.search});
    });

    window.addEventListener("popstate", function () {
        flushInputsNow();
        send({type: "navigate", path: location.pathname, query: location.search, replace: true});
    });

    document.addEventListener("click", function (e) {
        var t = e.target.closest("[data-rask-on-click]");
        if (!t || !inRoot(t)) return;
        e.preventDefault();
        flushInputsNow();
        send({
            id: t.getAttribute("data-rask-on-click"), type: "click",
            shiftKey: e.shiftKey, ctrlKey: e.ctrlKey, altKey: e.altKey, metaKey: e.metaKey
        });
    });

    // Input events fire per keystroke — on fast typing that's 5–10 WS frames per
    // second per input. Coalesce per-element with rAF: the same element typed into
    // multiple times within one frame produces a single outgoing message carrying
    // the latest value at flush time. The element itself is the de-duping key —
    // multiple inputs in the same frame each get one message. flushInputsNow() is
    // called at the top of every other event handler (change, submit, click,
    // navigate) so the server always processes input events before the subsequent
    // action that depends on them — without this, a change event triggered
    // immediately after typing reaches the server BEFORE the coalesced input, and
    // any validator the change kicks off reads the stale model value.
    var inputPending = new Set();
    var inputRaf = 0;

    function flushInputs() {
        inputRaf = 0;
        inputPending.forEach(function (el) {
            if (!el.isConnected) return;
            var id = el.getAttribute("data-rask-on-input");
            if (!id) return;
            send({id: id, type: "input", value: el.value});
        });
        inputPending.clear();
    }

    function flushInputsNow() {
        if (inputRaf) {
            cancelAnimationFrame(inputRaf);
            inputRaf = 0;
        }
        if (inputPending.size > 0) flushInputs();
    }

    function queueInput(el) {
        inputPending.add(el);
        if (!inputRaf) inputRaf = requestAnimationFrame(flushInputs);
    }

    document.addEventListener("input", function (e) {
        var t = e.target.closest("[data-rask-on-input]");
        if (!t || !inRoot(t)) return;
        // Inputs paired with data-rask-on-change need to dispatch SYNCHRONOUSLY: the
        // change event typically fires in the same task (Playwright fill, browser commit
        // on blur), and a downstream validator triggered by change reads the model state
        // set by the matching input. Coalescing the input would put the change event
        // ahead of it on the .NET dispatcher and the validator would observe stale
        // state. Only standalone input handlers (no change wired) get the rAF
        // coalescing win.
        if (t.hasAttribute("data-rask-on-change")) {
            send({id: t.getAttribute("data-rask-on-input"), type: "input", value: t.value});
            return;
        }
        queueInput(t);
    });

    document.addEventListener("change", function (e) {
        var t = e.target.closest("[data-rask-on-change], [data-rask-on-files]");
        if (!t || !inRoot(t)) return;
        // Flush before processing — if the same element (or a sibling) has a pending
        // coalesced input, the server needs to see it BEFORE the change-triggered
        // validator / handler runs, otherwise the validator reads stale model state.
        flushInputsNow();
        if (t.tagName === "INPUT" && t.type === "file" && t.hasAttribute("data-rask-on-files")) {
            var files = t.files;
            if (!files || files.length === 0) return;
            uploadFiles(files).then(function (metas) {
                send({id: t.getAttribute("data-rask-on-files"), type: "files", files: metas});
            }).catch(function (err) {
                console.error("Rask: file upload failed", err);
            });
            return;
        }
        if (t.hasAttribute("data-rask-on-change")) {
            send({id: t.getAttribute("data-rask-on-change"), type: "change", value: t.value});
        }
    });

    function uploadFiles(files) {
        var fd = new FormData();
        for (var i = 0; i < files.length; i++) {
            fd.append("f" + i, files[i], files[i].name);
            fd.append("f" + i + "__lastModified", String(files[i].lastModified || 0));
        }
        return fetch("/_rask/upload/" + encodeURIComponent(sessionId), {
            method: "POST",
            body: fd,
            credentials: "same-origin"
        }).then(function (res) {
            if (!res.ok) throw new Error("upload failed: " + res.status);
            return res.json();
        }).then(function (json) {
            return Array.isArray(json.files) ? json.files : [];
        });
    }

    function triggerDownload(url, filename) {
        var a = document.createElement("a");
        a.href = url;
        a.download = filename;
        a.style.display = "none";
        document.body.appendChild(a);
        a.click();
        setTimeout(function () {
            try {
                document.body.removeChild(a);
            } catch (_) {
            }
        }, 0);
    }

    // scroll events don't bubble — listen in capture phase at the document level so we
    // observe scroll on any descendant with [data-rask-on-scroll]. Coalesce bursts via
    // rAF: one outgoing message per frame per element, even if scroll fires 5–10x.
    var scrollPending = new Set();
    var scrollRaf = 0;

    function flushScroll() {
        scrollRaf = 0;
        scrollPending.forEach(function (el) {
            if (!el.isConnected) return;
            var id = el.getAttribute("data-rask-on-scroll");
            if (!id) return;
            send({
                id: id,
                type: "scroll",
                scrollTop: el.scrollTop | 0,
                clientHeight: el.clientHeight | 0,
                scrollHeight: el.scrollHeight | 0
            });
        });
        scrollPending.clear();
    }

    document.addEventListener("scroll", function (e) {
        var t = e.target;
        if (!t || t.nodeType !== 1) return;
        if (!t.hasAttribute || !t.hasAttribute("data-rask-on-scroll")) return;
        if (!inRoot(t)) return;
        scrollPending.add(t);
        if (!scrollRaf) scrollRaf = requestAnimationFrame(flushScroll);
    }, true);

    document.addEventListener("submit", function (e) {
        var t = e.target.closest("[data-rask-on-submit]");
        if (!t || !inRoot(t)) return;
        e.preventDefault();
        flushInputsNow();
        submitForm(t).catch(function (err) {
            console.error("Rask: submit failed", err);
        });
    });

    function submitForm(form) {
        var obj = {};
        var fileInputs = form.querySelectorAll('input[type="file"][name]');
        var pending = [];
        var fileFields = {};
        for (var i = 0; i < fileInputs.length; i++) {
            (function (input) {
                if (!input.files || input.files.length === 0) return;
                pending.push(uploadFiles(input.files).then(function (metas) {
                    fileFields[input.name] = metas;
                }));
            })(fileInputs[i]);
        }
        return Promise.all(pending).then(function () {
            var fd = new FormData(form);
            fd.forEach(function (v, k) {
                if (v instanceof File || v instanceof Blob) return;
                obj[k] = String(v);
            });
            if (Object.keys(fileFields).length > 0) obj.__files = fileFields;
            send({id: form.getAttribute("data-rask-on-submit"), type: "submit", form: obj});
        });
    }

    // ----- IJSRuntime global-JS dispatcher -----------------------------------
    // Mirrors the Microsoft.JSInterop contract: server sends an "identifier" like
    // "sessionStorage.getItem", we resolve it on window, invoke it with args, then
    // ship a jsResult back keyed by the server-assigned taskId. JSObjectReference
    // returns get a stable handle id; DotNetObjectReference values flow back via a
    // {__dotNetObject:<id>} placeholder so the .NET side can re-hydrate them.

    var jsObjectRefs = new Map();   // id -> target
    var nextJsObjectRefId = 1;

    function resolveIdentifier(target, identifier) {
        // Walk a dotted JS path on the given target (typically window). Returns
        // [parentObject, lastSegment] so the caller can preserve `this` when
        // calling methods (e.g. sessionStorage.setItem must run with sessionStorage
        // as `this`). Returns null on miss — caller throws.
        if (typeof identifier !== "string" || identifier.length === 0) return null;
        var parts = identifier.split(".");
        var parent = target;
        for (var i = 0; i < parts.length - 1; i++) {
            if (parent == null) return null;
            parent = parent[parts[i]];
        }
        if (parent == null) return null;
        var last = parts[parts.length - 1];
        return [parent, last];
    }

    function dispatchJsInvoke(inv) {
        if (!inv || typeof inv.identifier !== "string" || typeof inv.id !== "number") return;
        // Hold scoped-JS invokes until (a) the bundle script has executed and
        // the window.Rask.{TypeName} globals exist AND (b) every Head-declared
        // external <script src>/<link rel=stylesheet> has loaded. Only Rask.*
        // identifiers carry this risk — `sessionStorage.getItem`,
        // `localStorage.length`, etc. are browser-builtin and always present.
        if (inv.identifier.indexOf("Rask.") === 0 && (!scopedJsReady || !headAssetsReady())) {
            pendingScopedInvokes.push(inv);
            return;
        }
        var taskId = inv.id;
        var resultType = (typeof inv.resultType === "number") ? inv.resultType : 0;
        var argsJson = (typeof inv.argsJson === "string") ? inv.argsJson : "[]";
        var targetInstanceId = (typeof inv.targetInstanceId === "number") ? inv.targetInstanceId : 0;

        Promise.resolve().then(function () {
            var args;
            try {
                args = JSON.parse(argsJson, jsonReviver);
            } catch (e) {
                throw new Error("Failed to parse argsJson: " + e.message);
            }

            var target = window;
            if (targetInstanceId !== 0) {
                target = jsObjectRefs.get(targetInstanceId);
                if (!target) throw new Error("Unknown JS object reference: " + targetInstanceId);
            }

            var resolved = resolveIdentifier(target, inv.identifier);
            if (!resolved) throw new Error("Could not find '" + inv.identifier + "' on target");
            var parent = resolved[0];
            var key = resolved[1];
            var fn = parent[key];

            var result;
            if (typeof fn === "function") {
                result = fn.apply(parent, args);
            } else {
                // Identifier names a property (not a method) — return its value. This is
                // how blazor handles e.g. `localStorage.length`.
                result = fn;
            }
            return result;
        }).then(function (value) {
            // Mirrors Microsoft.JSInterop.JSCallResultType:
            //   0 = Default            — ship the value as-is.
            //   1 = JSObjectReference  — mint a handle id, send {__jsObjectId:<id>}.
            //   2 = JSStreamReference  — not supported yet; fall through to Default.
            //   3 = JSVoidResult       — drop the value, only the success ack matters.
            if (resultType === 3) {
                sendJsResult(taskId, true, null);
                return;
            }
            if (resultType === 1) {
                var refId = nextJsObjectRefId++;
                jsObjectRefs.set(refId, value);
                sendJsResult(taskId, true, {"__jsObjectId": refId});
                return;
            }
            sendJsResult(taskId, true, value);
        }).catch(function (err) {
            sendJsResult(taskId, false, null, (err && err.message) || String(err));
        });
    }

    function jsonReviver(key, value) {
        // Inverse of the placeholder write: replace {__jsObjectId:<id>} from the .NET
        // side with the live JS object. Skips other shapes.
        if (value && typeof value === "object" && typeof value.__jsObjectId === "number") {
            return jsObjectRefs.get(value.__jsObjectId);
        }
        return value;
    }

    function sendJsResult(id, success, result, error) {
        var msg = {type: "jsResult", id: id, success: success};
        if (success) {
            msg.result = result;
        } else {
            msg.error = error || "JS invocation failed";
        }
        send(msg);
    }

    // ----- Diff codec interpreter --------------------------------------------
    // Applies ops produced by C#-side FrameDiffer.Diff to the live DOM. Each op
    // names its target via a Path = sequence of childNodes indices from `document`.
    // The Path is computed by the diff walker counting only DOM-relevant frames
    // (Element, Text, Raw, Doctype) and excluding Attribute frames, which matches
    // the browser's `Node.childNodes` collection semantics for the rendered HTML.
    //
    // Each op is a positional array; the kind at op[0] selects which trailing slots
    // are present (mirrors LivePayload.BuildPayloadUtf8Diff exactly):
    //   1 SetAttribute     [k, path, name|idx, value]
    //   2 RemoveAttribute  [k, path, name|idx]
    //   3 UpdateText       [k, path, value]
    //   4 InsertSubtree    [k, path, html, domCount]
    //   5 RemoveSubtree    [k, path, domCount]
    //   6 MoveSubtree      [k, path, sourceSlot]
    //
    // Names for SetAttribute/RemoveAttribute may arrive as either a string (inline) or
    // a number that indexes into the optional payload-level "names" array — the server
    // interns names that appear 2+ times in the same payload to drop the duplicate
    // string bytes. resolveName() handles either form.
    // Comment nodes shift childNodes indices relative to the server's frame walk.
    // Filter to DOM-relevant nodes only (Element=1, Text=3, Doctype=10) so paths
    // match what FrameDiffer counts.
    var _relevantNodeTypes = { 1: 1, 3: 1, 10: 1 };

    function relevantChild(parent, index) {
        if (!parent || !parent.childNodes) return null;
        var seen = 0;
        for (var i = 0; i < parent.childNodes.length; i++) {
            var n = parent.childNodes[i];
            if (_relevantNodeTypes[n.nodeType]) {
                if (seen === index) return n;
                seen++;
            }
        }
        return null;
    }

    function resolvePath(path) {
        var node = document;
        for (var i = 0; i < path.length; i++) {
            node = relevantChild(node, path[i]);
            if (!node) return null;
        }
        return node;
    }

    // Mirror selected attribute writes onto the matching IDL property. After user
    // interaction, an input's `value` attribute is the *default*, not the current
    // state — setAttribute does not reach the live value. Same for `checked` on
    // checkboxes/radios and `selected` on options. Only sync when the element
    // supports the property so we don't silently no-op on unrelated tags.
    //
    // Active-element guard: when the diff would overwrite the value of the focused
    // input, the server's view is racing with the user's keystrokes (the server
    // rendered with a value computed before the latest key landed). Skipping the
    // sync on the focused element keeps the user's in-flight typing intact; the
    // next keystroke updates server state and any subsequent render reconciles.
    function syncFormProperty(el, name, value, isPresent) {
        // `isPresent` tells us whether the attribute is set or being removed —
        // separate from the value because the HTML attributes `checked`/`selected`
        // are presence-based: `<input checked>`, `<input checked="">`, and
        // `<input checked="checked">` all mean checked. RemoveAttribute → unchecked.
        if (!el) return;
        var tag = el.tagName;
        if (!tag) return;
        if (name === "value" && (tag === "INPUT" || tag === "TEXTAREA" || tag === "SELECT")) {
            if (document.activeElement === el) return;
            el.value = value;
        } else if (name === "checked" && tag === "INPUT") {
            el.checked = !!isPresent;
        } else if (name === "selected" && tag === "OPTION") {
            el.selected = !!isPresent;
        }
    }

    function applyDiff(ops, names) {
        function resolveName(raw) {
            // Server interns names that repeat 2+ times in the same payload — those
            // arrive as integer indices into the "names" array. Strings pass through.
            if (typeof raw === "number" && names) return names[raw];
            return raw;
        }

        for (var i = 0; i < ops.length; i++) {
            var op = ops[i];
            var k = op[0];
            var path = op[1] || [];
            switch (k) {
                case 1: { // SetAttribute [k, path, name|idx, value]
                    var el = resolvePath(path);
                    if (el && el.setAttribute) {
                        var name1 = resolveName(op[2]);
                        var rawVal = op[3];
                        var newVal = rawVal == null ? "" : rawVal;
                        el.setAttribute(name1, newVal);
                        // After a form-control has been interacted with, the value
                        // attribute is desynchronised from the .value/.checked property
                        // (the attribute is the *default*, not the current state). Sync
                        // the IDL property too so user-visible state matches the diff.
                        syncFormProperty(el, name1, newVal, true);
                    }
                    break;
                }
                case 2: { // RemoveAttribute [k, path, name|idx]
                    var el2 = resolvePath(path);
                    if (el2 && el2.removeAttribute) {
                        var name2 = resolveName(op[2]);
                        el2.removeAttribute(name2);
                        syncFormProperty(el2, name2, "", false);
                    }
                    break;
                }
                case 3: { // UpdateText [k, path, value]
                    var textNode = resolvePath(path);
                    if (textNode) {
                        // Works for Text nodes (nodeType 3) and elements alike. We assign
                        // .textContent (rather than .nodeValue) so the path resolving to
                        // a Raw-rendered element still gets cleared and refilled — Raw
                        // frames in the C# stream serialize verbatim markup into a
                        // single string, which corresponds to a sequence of DOM nodes
                        // the browser parsed. The current diff codec only emits
                        // UpdateText when both sides are the SAME kind (Text vs Text or
                        // Raw vs Raw), so textContent is the right knob.
                        var txtVal = op[2];
                        textNode.textContent = txtVal == null ? "" : txtVal;
                    }
                    break;
                }
                case 4: { // InsertSubtree [k, path, html, domCount]
                    var insertHtml = op[2];
                    if (typeof insertHtml !== "string") {
                        console.warn("[Rask] InsertSubtree without payload — server " +
                            "must include HTML fragment. Falling back to full reload.");
                        location.reload();
                        return;
                    }
                    var parentPath = path.slice(0, path.length - 1);
                    var slot = path[path.length - 1];
                    var parent = resolvePath(parentPath);
                    if (!parent) break;
                    var template = document.createElement("template");
                    template.innerHTML = insertHtml;
                    var refNode = parent.childNodes[slot] || null;
                    while (template.content.firstChild) {
                        parent.insertBefore(template.content.firstChild, refNode);
                    }
                    break;
                }
                case 5: { // RemoveSubtree [k, path, domCount]
                    var rmParentPath = path.slice(0, path.length - 1);
                    var rmSlot = path[path.length - 1];
                    var rmParent = resolvePath(rmParentPath);
                    if (!rmParent) break;
                    var removeCount = op[2] || 1;
                    for (var r = 0; r < removeCount; r++) {
                        var victim = rmParent.childNodes[rmSlot];
                        if (!victim) break;
                        rmParent.removeChild(victim);
                    }
                    break;
                }
                case 6: { // MoveSubtree [k, path, sourceSlot]
                    // Path encodes parent + destination slot; op[2] is the source slot.
                    // Detach the source node FIRST, then resolve the destination refNode
                    // in the post-detach sibling list — that matches how the server's
                    // keyed differ computes target indices (against the live DOM right
                    // before the move runs, with the moved node removed).
                    var mvParentPath = path.slice(0, path.length - 1);
                    var mvDst = path[path.length - 1];
                    var mvParent = resolvePath(mvParentPath);
                    if (!mvParent) break;
                    var mvSrcRaw = op[2];
                    var mvSrc = mvSrcRaw == null ? 0 : mvSrcRaw;
                    var mvNode = relevantChild(mvParent, mvSrc);
                    if (!mvNode) break;
                    mvParent.removeChild(mvNode);
                    var mvRef = relevantChild(mvParent, mvDst);
                    mvParent.insertBefore(mvNode, mvRef);
                    break;
                }
                default:
                    // Unknown op kind — newer server, older client. Bail to full reload
                    // so the user isn't stranded on a stale tree.
                    console.warn("[Rask] Unknown diff op kind: " + k);
                    location.reload();
                    return;
            }
        }
    }

    // ----- DotNet shim (mirror of Blazor's window.DotNet) --------------------
    // [JSInvokable] callbacks. JS code calls `DotNet.invokeMethodAsync("MyApp",
    // "MyMethod", arg1, arg2)`; we serialise args, ship a dotNetInvoke message,
    // and resolve the returned Promise when the server replies with dotNetResult.
    var dotNetPending = new Map();    // callId -> {resolve, reject}
    var nextDotNetCallId = 1;

    window.DotNet = window.DotNet || {
        invokeMethodAsync: function (assemblyName, methodIdentifier /*, ...args */) {
            var args = Array.prototype.slice.call(arguments, 2);
            var callId = String(nextDotNetCallId++);
            return new Promise(function (resolve, reject) {
                dotNetPending.set(callId, {resolve: resolve, reject: reject});
                send({
                    type: "dotNetInvoke",
                    callId: callId,
                    assemblyName: assemblyName,
                    methodIdentifier: methodIdentifier,
                    argsJson: JSON.stringify(args)
                });
            });
        },
        _endInvokeDotNet: function (msg) {
            var pending = dotNetPending.get(msg.callId);
            if (!pending) return;
            dotNetPending.delete(msg.callId);
            if (msg.success) {
                pending.resolve(msg.result);
            } else {
                pending.reject(new Error(msg.error || "DotNet invocation failed"));
            }
        }
    };

    // The reviveScript() + morph() definitions are concatenated in at build time by
    // the _RaskBuildClientJs target.
    // @@RASK_MORPH@@
})();
