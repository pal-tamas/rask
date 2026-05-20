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
            // The framework no longer auto-fires scoped-JS `rendered` hooks. C# user
            // code invokes them via `InvokeScopedJs(firstRender)` from OnRendered /
            // OnRenderedAsync; the resulting `scopedJsInvokes` payload field is
            // dispatched below in the WS message handler after each morph.
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
                // Dispatch any queued scoped-JS invocations against the freshly-morphed
                // DOM. Each entry calls `Rask.scoped.invoke(scope, method, id, args)`.
                // When `id` is present the dispatcher ships the result back via the
                // _sendResult bridge; otherwise it's fire-and-forget. The bundle
                // script is `defer`red and may not have loaded on the initial frame —
                // if so, the registry lookup misses and the dispatcher no-ops; the
                // applyScopedJs onload handler retries every registered scope.
                if (Array.isArray(data.scopedJsInvokes) && window.Rask && Rask.scoped) {
                    for (var si = 0; si < data.scopedJsInvokes.length; si++) {
                        var inv = data.scopedJsInvokes[si];
                        if (inv && typeof inv.scope === "string" && typeof inv.method === "string") {
                            var args = Array.isArray(inv.args) ? inv.args : [];
                            var invId = (typeof inv.id === "number") ? inv.id : null;
                            Rask.scoped.invoke(inv.scope, inv.method, invId, args);
                        }
                    }
                }
                if (typeof window.raskAfterMorph === "function") window.raskAfterMorph();
            }

            applyDom();
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

    // Install the InvokeJsAsync result-shipper. Server-side: route the result back
    // to the .NET JsInvokeResultStore over the live WS using a dedicated message
    // type. The framework's WS handler matches `id` and completes the awaiting TCS.
    if (window.Rask && Rask.scoped) {
        Rask.scoped._sendResult = function (id, value, error) {
            send({type: "invokeResult", id: id, result: value, error: error});
        };
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

    function applyScopedJs(hash) {
        var url = "/_rask/scoped.js?v=" + hash;
        var existing = document.querySelector("script[data-rask-scoped-js]");
        if (existing && existing.getAttribute("src") === url) return;
        // Replace rather than mutate src — browsers don't re-evaluate a <script>
        // on src change. The newly inserted script runs Rask.scoped.register(...)
        // calls; once loaded, force a walkUnmount + walkMount cycle so already-
        // mounted elements pick up new hook bodies after a hot-reload.
        var script = document.createElement("script");
        script.setAttribute("data-rask-scoped-js", "");
        script.defer = true;
        script.src = url;
        script.addEventListener("load", function () {
            // Bundle just loaded — register() calls have populated the registry. Any
            // C#-queued invocations that arrived before this point hit an empty
            // registry and no-op'd; re-invoke "rendered" against every registered
            // scope with firstRender=true (the bundle-load moment is the first time
            // these elements see their hook). Modules without a "rendered" export
            // silently no-op in the dispatcher.
            if (!window.Rask || !Rask.scoped) return;
            var marked = document.querySelectorAll("[data-rask-mount]");
            var seen = new Set();
            for (var i = 0; i < marked.length; i++) {
                var s = marked[i].getAttribute("data-rask-mount");
                if (s && !seen.has(s)) {
                    seen.add(s);
                    Rask.scoped.invoke(s, "rendered", null, [true]);
                }
            }
        }, {once: true});
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

    // The Rask.scoped dispatcher and the reviveScript() + morph() definitions are
    // concatenated in at build time by the _RaskBuildClientJs target. Order matters:
    // the dispatcher must be defined before morph() references it.
    // @@RASK_SCOPED@@
    // @@RASK_MORPH@@
})();
