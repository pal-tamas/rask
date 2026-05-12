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
        send({type: "navigate", path: url.pathname, query: url.search});
    });

    window.addEventListener("popstate", function () {
        send({type: "navigate", path: location.pathname, query: location.search, replace: true});
    });

    document.addEventListener("click", function (e) {
        var t = e.target.closest("[data-rask-on-click]");
        if (!t || !inRoot(t)) return;
        e.preventDefault();
        send({id: t.getAttribute("data-rask-on-click"), type: "click"});
    });

    document.addEventListener("input", function (e) {
        var t = e.target.closest("[data-rask-on-input]");
        if (!t || !inRoot(t)) return;
        send({id: t.getAttribute("data-rask-on-input"), type: "input", value: t.value});
    });

    document.addEventListener("change", function (e) {
        var t = e.target.closest("[data-rask-on-change]");
        if (!t || !inRoot(t)) return;
        send({id: t.getAttribute("data-rask-on-change"), type: "change", value: t.value});
    });

    document.addEventListener("submit", function (e) {
        var t = e.target.closest("[data-rask-on-submit]");
        if (!t || !inRoot(t)) return;
        e.preventDefault();
        var fd = new FormData(t);
        var obj = {};
        fd.forEach(function (v, k) {
            obj[k] = String(v);
        });
        send({id: t.getAttribute("data-rask-on-submit"), type: "submit", form: obj});
    });

    function morph(from, to) {
        if (from.nodeType !== to.nodeType || from.nodeName !== to.nodeName) {
            from.parentNode.replaceChild(to, from);
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
        if ((tag === "INPUT" || tag === "TEXTAREA") && document.activeElement !== from) {
            var newVal = to.getAttribute("value");
            if (newVal === null && to.tagName === "TEXTAREA") newVal = to.textContent;
            if (newVal === null) newVal = "";
            if (from.value !== newVal) from.value = newVal;
            var checked = to.hasAttribute("checked");
            if (from.checked !== checked) from.checked = checked;
        }
        var fc = [], tc = [];
        for (var n = from.firstChild; n; n = n.nextSibling) fc.push(n);
        for (var m = to.firstChild; m; m = m.nextSibling) tc.push(m);
        var max = Math.max(fc.length, tc.length);
        for (var k = 0; k < max; k++) {
            var src = fc[k], dst = tc[k];
            if (!src) from.appendChild(dst);
            else if (!dst) from.removeChild(src);
            else if (src.nodeType !== dst.nodeType || src.nodeName !== dst.nodeName) from.replaceChild(dst, src);
            else morph(src, dst);
        }
    }
})();
