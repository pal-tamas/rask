(function () {
    "use strict";

    // Built-in element-ref helpers, invoked from C# via ElementRef.FocusAsync/Blur/ScrollIntoView.
    // The JSON reviver resolves an ElementRef arg to the live DOM element, so each receives it.
    window.__raskEl = window.__raskEl || {
        focus: function (el) {
            if (el) el.focus();
        },
        blur: function (el) {
            if (el) el.blur();
        },
        scrollIntoView: function (el, opts) {
            if (el) el.scrollIntoView(opts || {behavior: "smooth", block: "nearest"});
        }
    };

    var root = document.querySelector("[data-rask-root]");
    if (!root) return;

    // Serializes render application across messages. A navigation diff may defer its
    // body swap until the new page's scoped CSS applies (waitForUnappliedHeadCss), which
    // opens a microtask/timer gap during which the next WS message could arrive. Both
    // the diff and full-HTML paths chain through this tail promise so a deferred body
    // always commits before the following message's ops — paths in a later diff are
    // computed against the render this one produces, so they must not be applied first.
    var _renderQueue = Promise.resolve();
    // The "#fragment" of an intercepted nav-link click. The fragment never leaves the
    // browser (the navigate message carries only path+query, and the server's history
    // url has no hash), so we stash it here on click and consume it when the matching
    // push reply commits — scroll to that anchor, else to the top. Cleared on consume.
    var _pendingScrollHash = "";
    // Hard cap on how long a navigation diff defers the body swap waiting for a newly
    // mounted page's scoped stylesheet to load. A warm content-addressed
    // /_rask/a/{hash}.css load resolves in a few ms; the cap only ever applies to a
    // genuinely slow/failed sheet, where we'd rather show the page than stall nav.
    var CSS_FOUC_GUARD_MS = 500;

    // Read once from an explicit <base href> element so the runtime can host
    // under a sub-path like /appA/ on a reverse proxy without the .NET side ever
    // seeing the prefix. Resolves to the directory portion of the base href.
    // When no <base> element is present we default to "/" — server-rendered
    // pages carry no <base>, and document.baseURI would otherwise fall back to
    // the current route URL (e.g. /realtime/BTC), yielding a bogus "/realtime/"
    // base that breaks the WS/asset URLs on every deep route.
    var basePath = null;

    function getBasePath() {
        if (basePath !== null) return basePath;
        var baseEl = document.querySelector("base[href]");
        if (!baseEl) {
            basePath = "/";
            return basePath;
        }
        var p = new URL(baseEl.href, location.href).pathname;
        var last = p.lastIndexOf("/");
        basePath = last < 0 ? "/" : p.slice(0, last + 1);
        return basePath;
    }

    function stripBase(pathname) {
        var b = getBasePath();
        if (b === "/" || !pathname) return pathname;
        if (pathname === b.slice(0, -1) || pathname === b) return "/";
        return pathname.indexOf(b) === 0 ? "/" + pathname.slice(b.length) : pathname;
    }

    function prependBase(url) {
        var b = getBasePath();
        if (b === "/" || typeof url !== "string" || url.charAt(0) !== "/" || url.indexOf(b) === 0) return url;
        return b + url.slice(1);
    }

    var sessionId = root.getAttribute("data-rask-root");
    var proto = location.protocol === "https:" ? "wss:" : "ws:";
    var baseWsUrl = proto + "//" + location.host + prependBase("/rask/ws");

    // JWT-on-WebSocket hook. Browsers can't set Authorization headers on a WS upgrade, so a
    // bearer-token app carries the access token on the URL as ?access_token= (the SignalR pattern;
    // pair it with AddJwtBearer's OnMessageReceived reading the query for the Rask WS path). The
    // token is read fresh on every (re)connect from window.Rask.authToken (string or function) or a
    // <meta name="rask-access-token"> tag. With no token set this is a no-op — cookie auth is
    // unaffected and the URL is unchanged.
    function buildWsUrl() {
        var token = null;
        try {
            var r = window.Rask;
            if (r && typeof r.authToken === "function") token = r.authToken();
            else if (r && typeof r.authToken === "string") token = r.authToken;
            if (!token) {
                var meta = document.querySelector('meta[name="rask-access-token"]');
                if (meta) token = meta.getAttribute("content");
            }
        } catch (e) {
            token = null;
        }
        if (!token) return baseWsUrl;
        return baseWsUrl + (baseWsUrl.indexOf("?") >= 0 ? "&" : "?") + "access_token=" + encodeURIComponent(token);
    }

    var ws = null;
    var queue = [];
    var open = false;
    var attempt = 0;
    var reconnectTimer = null;
    var suppressEvents = false;
    var overlay = installOverlay();
    // The reconnect overlay doubles as the auth-handshake indicator. During a sign-in/out the
    // socket is deliberately closed and reconnected to pick up the new cookie; that reconnect is
    // an authentication step, not a dropped connection, so the overlay says "Authenticating…"
    // instead of "Reconnecting…" for its duration.
    var overlayMsg = overlay.querySelector(".rask-overlay__msg");
    var authInProgress = false;
    var RECONNECT_MSG = "Reconnecting…";
    var AUTH_MSG = "Authenticating…";

    function setOverlayMessage(text) {
        if (overlayMsg) overlayMsg.textContent = text;
    }

    connect();

    function connect() {
        ws = new WebSocket(buildWsUrl());

        ws.addEventListener("open", function () {
            open = true;
            attempt = 0;
            suppressEvents = false;
            ws.send(JSON.stringify({type: "hello", session: sessionId}));
            for (var i = 0; i < queue.length; i++) ws.send(queue[i]);
            queue.length = 0;
            // Auth reconnect completed — restore the default message for any future drop.
            if (authInProgress) {
                authInProgress = false;
                setOverlayMessage(RECONNECT_MSG);
            }
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
            // Handler ack: resolve the slow-link pending bar. Handled synchronously here
            // (not inside _renderQueue) so a CSS-gated deferred body swap can't keep the
            // bar up after the round-trip has actually completed.
            if (data.type === "ack") {
                satisfySeq(data.seq);
                return;
            }
            // Diff-mode payload (kind:"diff"): apply ops directly against the live DOM.
            // Both render paths chain through _renderQueue so a diff that defers its body
            // for a scoped-CSS load (see applyDiffReply) can't be overtaken by the next
            // message — paths in a later diff are computed against this render's output.
            if (data.kind === "diff" && Array.isArray(data.ops)) {
                _renderQueue = _renderQueue.then(function () {
                        return applyDiffReply(data);
                    },
                    function () {
                        return applyDiffReply(data);
                    });
                return;
            }
            _renderQueue = _renderQueue.then(function () {
                    return applyFullReply(data);
                },
                function () {
                    return applyFullReply(data);
                });
        });

        ws.addEventListener("close", scheduleReconnect);
        ws.addEventListener("error", scheduleReconnect);
    }

    function scheduleReconnect() {
        if (reconnectTimer !== null) return;
        open = false;
        resetPending();
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
            '<span class="rask-overlay__msg">Reconnecting…</span>' +
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

    // Slow-link pending-action indicator. A handler event (click/input/change/submit)
    // is tagged with a monotonic seq; the server replies {type:"ack",seq} once it has
    // processed the dispatch — crucially even when the render dedupes and ships no frame.
    // If no ack lands within PENDING_LATENCY_MS we surface a thin top-of-viewport bar so
    // a high-latency user sees that their action registered; it clears when the matching
    // (or any later) ack arrives. A hard timeout backstops a genuinely lost frame. This
    // is distinct from — and sits one z-index below — the full reconnect overlay above.
    var PENDING_LATENCY_MS = 300;
    var PENDING_HARD_TIMEOUT_MS = 10000;
    var seqCounter = 0;
    var outstandingSeq = 0;
    var ackedSeq = 0;
    var pendingTimer = null;
    var pendingHardTimer = null;
    var pendingVisible = false;
    var pendingBar = installPendingBar();

    function stampSeq(payload) {
        // Only genuine handler events get a seq: they carry an `id` and dispatch through
        // the server's handler chain, which acks the seq. jsResult also carries an id but
        // is an interop reply (not a handler) — exclude it; navigate/dotNetInvoke/hello
        // carry no id and so are excluded too.
        if (!payload || payload.id == null || payload.type === "jsResult") return;
        payload.seq = ++seqCounter;
        outstandingSeq = payload.seq;
        if (pendingTimer === null && !pendingVisible) {
            pendingTimer = setTimeout(showPendingBar, PENDING_LATENCY_MS);
        }
        if (pendingHardTimer !== null) clearTimeout(pendingHardTimer);
        pendingHardTimer = setTimeout(forcePendingTimeout, PENDING_HARD_TIMEOUT_MS);
    }

    function satisfySeq(s) {
        if (typeof s !== "number") return;
        if (s > ackedSeq) ackedSeq = s;
        if (ackedSeq >= outstandingSeq) clearPending();
    }

    function clearPending() {
        if (pendingTimer !== null) {
            clearTimeout(pendingTimer);
            pendingTimer = null;
        }
        if (pendingHardTimer !== null) {
            clearTimeout(pendingHardTimer);
            pendingHardTimer = null;
        }
        hidePendingBar();
    }

    function resetPending() {
        // On disconnect the reconnect overlay takes over; drop the bar and treat every
        // outstanding handler as settled so a pre-drop seq can't wedge the next session.
        ackedSeq = outstandingSeq = seqCounter;
        clearPending();
    }

    function forcePendingTimeout() {
        // Backstop: no ack came back for an outstanding handler (lost frame or a hung
        // handler). Settle and hide rather than leave the bar wedged.
        ackedSeq = outstandingSeq = seqCounter;
        clearPending();
    }

    function showPendingBar() {
        pendingTimer = null;
        pendingVisible = true;
        if (pendingBar) pendingBar.setAttribute("data-show", "");
    }

    function hidePendingBar() {
        pendingVisible = false;
        if (pendingBar) pendingBar.removeAttribute("data-show");
    }

    function installPendingBar() {
        // Managed siblings of the server-rendered tree (data-rask-managed), so the morph
        // diff treats them as invisible and never trims them — same convention as the
        // reconnect overlay.
        var style = document.createElement("style");
        style.setAttribute("data-rask-pending", "");
        style.setAttribute("data-rask-managed", "");
        style.textContent =
            ".rask-pending{position:fixed;top:0;left:0;right:0;height:2px;" +
            "z-index:2147483646;pointer-events:none;overflow:hidden;display:none;}" +
            ".rask-pending[data-show]{display:block;}" +
            ".rask-pending__bar{position:absolute;top:0;left:0;height:100%;width:40%;" +
            "background:linear-gradient(90deg,rgba(124,58,237,0),#7C3AED,rgba(124,58,237,0));" +
            "animation:rask-pending-slide 1s ease-in-out infinite;}" +
            "@keyframes rask-pending-slide{0%{transform:translateX(-100%);}" +
            "100%{transform:translateX(350%);}}";
        document.head.appendChild(style);

        var el = document.createElement("div");
        el.className = "rask-pending";
        el.setAttribute("data-rask-managed", "");
        el.setAttribute("data-rask-pending", "");
        el.setAttribute("aria-hidden", "true");
        el.innerHTML = '<div class="rask-pending__bar"></div>';
        document.documentElement.appendChild(el);
        return el;
    }

    // scopedJsReady starts true: per-component scripts ship as
    // <script src="/_rask/a/{hash}.js" defer> tags in the initial HTML's <head> (and
    // are morphed in/out as components mount/unmount). The browser's defer semantics
    // run them in document order before DOMContentLoaded, which is well before any
    // user click could trigger a Rask.* invoke. The legacy bundle-based gate that
    // waited for a single big script to load is gone with the bundle endpoint itself.
    var scopedJsReady = true;
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
        // Per-component scoped tags carry a data-rask-key with the framework's
        // reserved "rsk-" prefix. They're served from /_rask/a/{hash}.{ext} with
        // long-lived immutable caching — their load is essentially synchronous on
        // warm cache, and the user-facing Rask.* invoke deferral logic doesn't
        // need to track them. Skip so we don't bloat pendingHeadAssets.
        var keyAttr = el.getAttribute("data-rask-key");
        if (keyAttr && keyAttr.indexOf("rsk-") === 0) return;
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
        el.addEventListener("load", function () {
            finish("load");
        }, {once: true});
        el.addEventListener("error", function () {
            finish("error");
        }, {once: true});
        // Safety: the load/error event may have fired between insertion and
        // our listener attach (cache hit on a CDN). Performance.getEntriesByName
        // covers the common case; the timeout covers everything else so a
        // missed event doesn't hold Rask.* invokes forever.
        setTimeout(function () {
            finish("timeout");
        }, HEAD_ASSET_LOAD_TIMEOUT_MS);
    }

    function scanHeadAssets() {
        var els = document.head.querySelectorAll("script[src], link[rel=stylesheet]");
        for (var i = 0; i < els.length; i++) trackHeadAsset(els[i]);
    }

    function headAssetsReady() {
        return pendingHeadAssets.size === 0;
    }

    // Return a Promise that resolves once every <head> stylesheet still being applied has
    // reached a terminal state (load / error / CSS_FOUC_GUARD_MS timeout), or null when
    // there's nothing to wait for. The readiness signal is the <link>'s .sheet property —
    // non-null only once the CSSOM stylesheet has been parsed and APPLIED. We deliberately
    // do NOT use isAssetAlreadyLoaded (Resource Timing responseEnd): the eager
    // <link rel="prefetch"> warms the HTTP cache and creates a timing entry, but bytes
    // downloaded is not the same as a stylesheet applied — trusting it would skip the wait
    // and reintroduce the very flash prefetch is meant to remove. A link already applied
    // (kept across renders, or just resolved) has a non-null .sheet and is skipped; a
    // freshly inserted one has .sheet === null and is awaited (its load fires within ~1
    // frame on warm cache).
    function waitForUnappliedHeadCss() {
        var pending = [];
        document.head.querySelectorAll('link[rel="stylesheet"]').forEach(function (l) {
            if (!l.href || l.sheet) return;
            pending.push(new Promise(function (resolve) {
                var done = function () {
                    resolve();
                };
                l.addEventListener("load", done, {once: true});
                l.addEventListener("error", done, {once: true});
                setTimeout(done, CSS_FOUC_GUARD_MS);
            }));
        });
        return pending.length ? Promise.all(pending) : null;
    }

    // FOUC guard for the full-document path. A full reply morphs <head> and the styled
    // <body> in one pass, so a newly mounted component's scoped <link> would be inserted
    // alongside the body it styles — and the body paints before the just-inserted sheet
    // parses + applies. Pre-empt it: for every NEW scoped stylesheet the incoming document
    // adds to <head> (keyed by data-rask-key, so not already live), append a clone NOW and
    // return a Promise that resolves once each has applied (.sheet) — load / error /
    // CSS_FOUC_GUARD_MS timeout. The subsequent morph matches each clone to the incoming
    // <link> by key (keyed reconciliation), so it's kept rather than duplicated, and the
    // body it morphs in paints already-styled. Only keyed scoped links are preloaded —
    // render-blocking globals (no data-rask-key) are already applied from the initial load.
    // Returns null when the document adds no new scoped stylesheet (the common case), so a
    // navigation that mounts nothing new keeps today's single-pass, no-wait timing.
    function preloadNewHeadStylesheets(freshHtml) {
        var freshHead = freshHtml.querySelector("head");
        if (!freshHead) return null;
        var liveKeys = {};
        document.head.querySelectorAll('link[rel="stylesheet"][data-rask-key]').forEach(function (l) {
            liveKeys[l.getAttribute("data-rask-key")] = true;
        });
        var pending = [];
        freshHead.querySelectorAll('link[rel="stylesheet"][data-rask-key]').forEach(function (fl) {
            if (liveKeys[fl.getAttribute("data-rask-key")] || !fl.getAttribute("href")) return;
            var clone = fl.cloneNode(true);
            document.head.appendChild(clone);
            pending.push(new Promise(function (resolve) {
                var done = function () {
                    resolve();
                };
                clone.addEventListener("load", done, {once: true});
                clone.addEventListener("error", done, {once: true});
                setTimeout(done, CSS_FOUC_GUARD_MS);
            }));
        });
        return pending.length ? Promise.all(pending) : null;
    }

    // Reset scroll on forward navigation only (history.action "push" — a nav-link click
    // or Navigator.Navigate). "replace" (Back/Forward popstate, SetQuery, auth redirect)
    // is left to the browser's native scroll restoration. When the intercepted link
    // carried a "#fragment" matching an element, scroll there instead of the top.
    // Call this only after the new body has committed so the anchor target exists.
    function applyNavScroll(history) {
        if (!history || history.action === "replace") {
            _pendingScrollHash = "";
            return;
        }
        var hash = _pendingScrollHash;
        _pendingScrollHash = "";
        if (hash && hash.length > 1) {
            var el = null;
            try {
                el = document.querySelector(hash) ||
                    document.getElementById(decodeURIComponent(hash.slice(1)));
            } catch (e) {
                el = null;
            }
            if (el) {
                el.scrollIntoView();
                return;
            }
        }
        window.scrollTo(0, 0);
    }

    // Diff-mode render application (wire format matches LivePayload.BuildPayloadUtf8Diff).
    // The head rides the payload as a <head> fragment (user Head contributions are collected
    // + spliced server-side, so they're not in the frame stream). Morph the head FIRST —
    // keyed reconciliation (data-rask-key) keeps unchanged scoped-CSS links — and when it
    // adds a not-yet-applied scoped stylesheet, defer the body ops until it applies so the
    // swapped body never paints unstyled (FOUC). Returns the wait Promise so _renderQueue
    // holds the next message until the body has committed.
    function applyDiffReply(data) {
        var applyBody = function () {
            // Each op carries a Path (childNodes indices from the document root) and an
            // op-specific payload.
            applyDiff(data.ops, Array.isArray(data.names) ? data.names : null);
            if (data.history && typeof data.history.url === "string") {
                var diffTarget = prependBase(data.history.url);
                if (data.history.action === "replace") {
                    history.replaceState({rask: true}, "", diffTarget);
                } else {
                    if (_pendingScrollHash) diffTarget += _pendingScrollHash;
                    history.pushState({rask: true}, "", diffTarget);
                }
            }
            applyNavScroll(data.history);
            // Re-scan so newly-added scoped <script>/<link> feed the Rask.* invoke gate.
            scanHeadAssets();
            // Fire-and-forget IJSRuntime invokes ride the diff payload too (e.g. a
            // scoped-JS OnRenderedAsync hook); dispatch them via the shared loop so the
            // per-namespace deferral inside dispatchJsInvoke keeps working.
            applyFrameInvokes(data, dispatchJsInvoke);
            if (typeof window.raskAfterMorph === "function") window.raskAfterMorph();
        };
        if (typeof data.head === "string") {
            var freshHead = new DOMParser().parseFromString(data.head, "text/html").head;
            if (freshHead) {
                morph(document.head, freshHead);
                var wait = waitForUnappliedHeadCss();
                if (wait) return wait.then(applyBody);
            }
        }
        applyBody();
    }

    // Full-document render application: morph the whole <html> element so head changes
    // (title, per-page Head asset contributions, scoped-css/scoped-js hash bumps) propagate
    // with the body. A newly mounted component's scoped <link> rides this path too — and the
    // single morph would insert it alongside the styled body it applies to, painting unstyled
    // for a beat (FOUC). So preloadNewHeadStylesheets first appends + awaits a clone of each
    // new scoped stylesheet; the morph then matches the clone by data-rask-key (kept, not
    // duplicated) and the body it paints is already styled. Returns the wait Promise so
    // _renderQueue holds the next message until the body has committed.
    function applyFullReply(data) {
        var freshHtml = null;
        if (typeof data.html === "string") {
            var doc = new DOMParser().parseFromString(data.html, "text/html");
            freshHtml = doc.documentElement;
        }

        var commit = function () {
            if (freshHtml) {
                morph(document.documentElement, freshHtml);
                root = document.querySelector("[data-rask-root]") || root;
                // Pick up any newly-inserted Head-declared external assets (e.g., a
                // page-specific Script in Component.Head) so their load events feed the gate.
                scanHeadAssets();
            }
            if (data.history && typeof data.history.url === "string") {
                var fullTarget = prependBase(data.history.url);
                if (data.history.action === "replace") {
                    history.replaceState({rask: true}, "", fullTarget);
                } else {
                    if (_pendingScrollHash) fullTarget += _pendingScrollHash;
                    history.pushState({rask: true}, "", fullTarget);
                }
            }
            applyNavScroll(data.history);
            applyFrameInvokes(data, dispatchJsInvoke);
            // dotNetResult: reply to a JS-initiated DotNet.invokeMethodAsync call, routed by
            // the DotNet shim's pending-call table to resolve/reject the matching JS Promise.
            if (data.type === "dotNetResult" && typeof data.callId === "string") {
                window.DotNet._endInvokeDotNet(data);
            }
            if (typeof window.raskAfterMorph === "function") window.raskAfterMorph();
            // Out-of-band frames carry no html — process supplemental fields and bail.
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
        };

        // FOUC guard: preload any new scoped stylesheet the incoming document adds so the
        // morph below paints the styled body only once its sheet has applied (see
        // preloadNewHeadStylesheets). Returns null — and we commit synchronously, at today's
        // timing — when the render mounts no new scoped CSS.
        if (freshHtml) {
            var wait = preloadNewHeadStylesheets(freshHtml);
            if (wait) return wait.then(commit);
        }
        commit();
    }

    function maybeDrainPendingInvokes() {
        // scopedJsReady is permanently true post-cutover; the gate remains here so
        // user-Head-declared deps (the pendingHeadAssets path) can still pause
        // Rask.* invokes until their CDN scripts have loaded.
        if (!scopedJsReady || !headAssetsReady()) return;
        if (pendingScopedInvokes.length === 0) return;
        // Re-queue any invoke whose Rask.{Name} namespace still hasn't appeared —
        // the polling loop drains them when (if) the per-component script loads.
        var stillWaiting = [];
        var ready = [];
        for (var i = 0; i < pendingScopedInvokes.length; i++) {
            var inv = pendingScopedInvokes[i];
            if (raskNamespaceReady(inv.identifier)) ready.push(inv);
            else stillWaiting.push(inv);
        }
        pendingScopedInvokes = stillWaiting;
        for (var j = 0; j < ready.length; j++) {
            dispatchJsInvoke(ready[j]);
        }
    }

    function raskNamespaceReady(identifier) {
        if (typeof identifier !== "string") return true;
        if (identifier.indexOf("Rask.") !== 0) return true;
        var rest = identifier.substring(5);
        var dot = rest.indexOf(".");
        var name = dot < 0 ? rest : rest.substring(0, dot);
        return !!(window.Rask && window.Rask[name]);
    }

    // Per-component scripts load asynchronously from /_rask/a/{hash}.js. A first-render
    // Rask.* invoke races their load; the parked invoke wakes when window.Rask.{TypeName}
    // appears (or after the 5s timeout, in which case the original "Could not find" surfaces).
    var RASK_NS_POLL_INTERVAL_MS = 100;
    var RASK_NS_POLL_TIMEOUT_MS = 5000;
    var raskNsPollHandle = 0;
    var raskNsPollStarted = 0;

    function ensureRaskNamespacePoll() {
        if (raskNsPollHandle !== 0) return;
        raskNsPollStarted = Date.now();
        raskNsPollHandle = setInterval(function () {
            if (pendingScopedInvokes.length === 0
                || Date.now() - raskNsPollStarted > RASK_NS_POLL_TIMEOUT_MS) {
                clearInterval(raskNsPollHandle);
                raskNsPollHandle = 0;
                var drained = pendingScopedInvokes;
                pendingScopedInvokes = [];
                for (var i = 0; i < drained.length; i++) {
                    // After timeout, force-dispatch through the post-gate body so the
                    // original "Could not find" surface (caught by the user's ErrorBoundary)
                    // beats hanging forever on a broken asset URL.
                    forceDispatchJsInvoke(drained[i]);
                }
                return;
            }
            maybeDrainPendingInvokes();
        }, RASK_NS_POLL_INTERVAL_MS);
    }

    // Initial sweep for Head-declared external assets emitted by the server's
    // first GET. New assets added by morph (e.g., a page-specific Head script)
    // are picked up in applyDom() after the morph completes.
    scanHeadAssets();

    function send(payload) {
        if (suppressEvents) return;
        stampSeq(payload);
        var msg = JSON.stringify(payload);
        if (open && ws && ws.readyState === WebSocket.OPEN) ws.send(msg);
        else queue.push(msg);
    }

    function redeemAuthTicket(auth) {
        suppressEvents = true;
        // The imminent socket close + reconnect is an auth step — show "Authenticating…" up front
        // so the user never sees "Reconnecting…" for a deliberate sign-in/out.
        authInProgress = true;
        setOverlayMessage(AUTH_MSG);
        showOverlay();
        fetch(prependBase("/_rask/auth/redeem"), {
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
        // Stash the link's "#fragment" so applyNavScroll can scroll to the anchor once
        // the new page commits (the fragment is not sent to the server).
        _pendingScrollHash = url.hash || "";
        flushInputsNow();
        send({type: "navigate", path: stripBase(url.pathname), query: url.search});
    });

    window.addEventListener("popstate", function () {
        flushInputsNow();
        send({type: "navigate", path: stripBase(location.pathname), query: location.search, replace: true});
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
            // For a checkbox the meaningful state is el.checked, not el.value (which is the
            // static "on" default). Report it as "true"/"false" so bound checkboxes set the
            // model to the actual state (self-correcting) instead of relying on a server-side
            // toggle. Radios and text inputs keep sending el.value (a radio's value IS the
            // selected option).
            var changeVal = (t.tagName === "INPUT" && t.type === "checkbox")
                ? (t.checked ? "true" : "false")
                : t.value;
            // Record the PRE-EDIT value (the last server-rendered `value` attribute)
            // so a lagging re-render carrying that stale value can't clobber the
            // user's fresh edit before the server's authoritative response lands —
            // see raskShouldSuppressValue. Checkboxes self-correct via the checked
            // path, so they don't participate in the value guard.
            if (!(t.tagName === "INPUT" && t.type === "checkbox")) {
                var sv = t.getAttribute("value");
                raskNotePendingValue(t, sv === null ? "" : sv);
            }
            send({id: t.getAttribute("data-rask-on-change"), type: "change", value: changeVal});
        }
    });

    function uploadFiles(files) {
        var fd = new FormData();
        for (var i = 0; i < files.length; i++) {
            fd.append("f" + i, files[i], files[i].name);
            fd.append("f" + i + "__lastModified", String(files[i].lastModified || 0));
        }
        return fetch(prependBase("/_rask/upload/" + encodeURIComponent(sessionId)), {
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
        // url is framework-built (/_rask/download/...); resolve + reject anything
        // that isn't same-origin so a javascript:/cross-origin href can never land here.
        var resolved;
        try {
            resolved = new URL(url, location.href);
        } catch (_) {
            return;
        }
        if (resolved.origin !== location.origin) return;
        var a = document.createElement("a");
        a.href = resolved.href;
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

    // ----- Drag & drop -------------------------------------------------------
    // HTML5 native DnD bound to parameterless C# handlers (same dispatch path as click). The
    // dragged item's identity rides the handler's closure, not the payload, so messages carry
    // only {id, type}. dragstart must seed dataTransfer so the drag is valid in Firefox; dragover
    // must preventDefault on a drop target or the browser rejects the drop. The optional
    // data-rask-on-dragover round-trip drives a server-rendered drop-target highlight — deduped
    // to one message per hovered element so a continuous dragover stream doesn't flood the socket.
    var lastDragOverEl = null;

    document.addEventListener("dragstart", function (e) {
        var t = e.target.closest("[data-rask-on-dragstart]");
        if (!t || !inRoot(t)) return;
        if (e.dataTransfer) {
            try {
                e.dataTransfer.setData("text/plain", "");
            } catch (err) {
            }
            e.dataTransfer.effectAllowed = "move";
        }
        lastDragOverEl = null;
        send({id: t.getAttribute("data-rask-on-dragstart"), type: "dragstart"});
    });

    document.addEventListener("dragover", function (e) {
        var t = e.target.closest("[data-rask-on-drop], [data-rask-on-dragover]");
        if (!t || !inRoot(t)) return;
        // preventDefault is what marks this element as a valid drop target.
        e.preventDefault();
        if (e.dataTransfer) e.dataTransfer.dropEffect = "move";
        if (!t.hasAttribute("data-rask-on-dragover")) return;
        if (t === lastDragOverEl) return; // dedupe: only notify when the hovered target changes
        lastDragOverEl = t;
        send({id: t.getAttribute("data-rask-on-dragover"), type: "dragover"});
    });

    document.addEventListener("drop", function (e) {
        var t = e.target.closest("[data-rask-on-drop]");
        if (!t || !inRoot(t)) return;
        e.preventDefault();
        lastDragOverEl = null;
        send({id: t.getAttribute("data-rask-on-drop"), type: "drop"});
    });

    document.addEventListener("dragend", function (e) {
        lastDragOverEl = null;
        var t = e.target.closest("[data-rask-on-dragend]");
        if (!t || !inRoot(t)) return;
        send({id: t.getAttribute("data-rask-on-dragend"), type: "dragend"});
    });

    // Keyboard: keydown/keyup dispatch to the nearest ancestor carrying a handler (focus-scoped,
    // like click). Never preventDefault — a key handler composes with normal typing; the C# side
    // decides what a key means. flushInputsNow first so an Enter-to-submit handler reads the value
    // the user just typed, not the pre-flush one. Modifier flags + repeat ride along for shortcuts.
    function sendKey(e, attr, type) {
        var t = e.target.closest ? e.target.closest("[" + attr + "]") : null;
        if (!t || !inRoot(t)) return;
        flushInputsNow();
        send({
            id: t.getAttribute(attr), type: type,
            key: e.key, code: e.code, repeat: e.repeat,
            shiftKey: e.shiftKey, ctrlKey: e.ctrlKey, altKey: e.altKey, metaKey: e.metaKey
        });
    }

    document.addEventListener("keydown", function (e) {
        sendKey(e, "data-rask-on-keydown", "keydown");
    });

    document.addEventListener("keyup", function (e) {
        sendKey(e, "data-rask-on-keyup", "keyup");
    });

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
        // Hold scoped-JS invokes until (a) every user-Head-declared CDN dep has loaded AND
        // (b) the per-component <script src="/_rask/a/{hash}.js"> has executed (so
        // window.Rask.{TypeName} exists). The polling tick wakes parked invokes when the
        // namespace appears, or surfaces the original "Could not find" after the 5s timeout.
        if (inv.identifier.indexOf("Rask.") === 0
            && (!scopedJsReady || !headAssetsReady() || !raskNamespaceReady(inv.identifier))) {
            pendingScopedInvokes.push(inv);
            ensureRaskNamespacePoll();
            return;
        }
        forceDispatchJsInvoke(inv);
    }

    function forceDispatchJsInvoke(inv) {
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
        if (value && typeof value === "object") {
            if (typeof value.__jsObjectId === "number") {
                return jsObjectRefs.get(value.__jsObjectId);
            }
            // ElementRef: {"__raskRef__":"id"} -> the live DOM element (or null if not in the DOM).
            if (typeof value.__raskRef__ === "string") {
                return document.querySelector('[data-rask-ref="' + value.__raskRef__ + '"]');
            }
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

    // @@RASK_DOM@@

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
