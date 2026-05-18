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
            var fresh = null;
            if (typeof data.html === "string") {
                var doc = new DOMParser().parseFromString(data.html, "text/html");
                fresh = doc.body;
            }
            function applyDom() {
                if (fresh) {
                    morph(root, fresh);
                    root = document.querySelector("[data-rask-root]") || root;
                }
                if (data.history && typeof data.history.url === "string") {
                    if (data.history.action === "replace") {
                        history.replaceState({rask: true}, "", data.history.url);
                    } else {
                        history.pushState({rask: true}, "", data.history.url);
                    }
                }
                if (typeof window.raskAfterMorph === "function") window.raskAfterMorph();
            }
            // Animate navigations (renders carrying a history block) with the View
            // Transitions API when the browser supports it. State-only re-renders
            // (no history) skip the wrap to keep event-handler latency tight.
            if (data.history && typeof document.startViewTransition === "function") {
                document.startViewTransition(applyDom);
            } else {
                applyDom();
            }
            if (typeof data.cssHash === "string") applyScopedCss(data.cssHash);
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
        var style = document.createElement("style");
        style.setAttribute("data-rask-overlay", "");
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
        send({id: t.getAttribute("data-rask-on-click"), type: "click",
              shiftKey: e.shiftKey, ctrlKey: e.ctrlKey, altKey: e.altKey, metaKey: e.metaKey});
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
        if (inputRaf) { cancelAnimationFrame(inputRaf); inputRaf = 0; }
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
            try { document.body.removeChild(a); } catch (_) {}
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

    // The reviveScript() + morph() definitions are concatenated in at build time
    // from Rask.Core/Resources/rask-morph.js by the _RaskBuildClientJs target.
    // @@RASK_MORPH@@
})();
