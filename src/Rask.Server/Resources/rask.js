(function () {
    "use strict";

    // Singleton guard. The runtime is a per-document singleton that binds global document-level event
    // listeners (click/input/change/submit) and owns the WS. A client-side navigation that applies a
    // full-HTML reply can re-insert the runtime <script src> (morph re-adds it), re-executing this IIFE
    // in the SAME document — every extra instance binds another set of document listeners, so a single
    // click/input then dispatches to the server once per instance (handlers double-fire; e.g. a dropdown
    // toggle opens then immediately closes). Bail if we have already booted this document; a real full
    // page load resets window, so this only dedupes spurious in-document re-execution, never a
    // legitimately fresh page.
    if (window.__raskBooted) return;
    window.__raskBooted = true;

    // Shared framework interop helpers (__raskEl, __raskApi) spliced from
    // Rask.Core/Resources/rask-api.js at build time — single source across both transports.
    // @@RASK_API@@

    // Transport-agnostic PWA helpers (__raskPush/__raskNotify/__raskBadge/__raskWakeLock) spliced from
    // Rask.Core/Resources/rask-pwa.js — the same source the WASM client uses. They assign to window.* so
    // the IWebPush/INotifications/IBadge/IWakeLock services reach them. Inert unless AddRaskPwa is used.
    // @@RASK_PWA@@

    let root = document.querySelector("[data-rask-root]");
    if (!root) return;

    // Development-only affordances gate on this. The server stamps data-rask-dev onto <body> only
    // when the app is in Development AND running under `dotnet watch`, so in production the flag is
    // absent and every branch below it is unreachable — even if a dev frame somehow arrived.
    const devMode = root.hasAttribute("data-rask-dev");

    // Serializes render application across messages. A navigation diff may defer its
    // body swap until the new page's scoped CSS applies (waitForUnappliedHeadCss), which
    // opens a microtask/timer gap during which the next WS message could arrive. Both
    // the diff and full-HTML paths chain through this tail promise so a deferred body
    // always commits before the following message's ops — paths in a later diff are
    // computed against the render this one produces, so they must not be applied first.
    let _renderQueue = Promise.resolve();
    // The "#fragment" of an intercepted nav-link click. The fragment never leaves the
    // browser (the navigate message carries only path+query, and the server's history
    // url has no hash), so we stash it here on click and consume it when the matching
    // push reply commits — scroll to that anchor, else to the top. Cleared on consume.
    let _pendingScrollHash = "";
    // CSS_FOUC_GUARD_MS + the scoped-CSS FOUC gating functions (waitForUnappliedHeadCss /
    // preloadNewHeadStylesheets) are spliced in below from rask-scoped.js (@@RASK_SCOPED@@).

    // Read once from an explicit <base href> element so the runtime can host
    // under a sub-path like /appA/ on a reverse proxy without the .NET side ever
    // seeing the prefix. Resolves to the directory portion of the base href.
    // When no <base> element is present we default to "/" — server-rendered
    // pages carry no <base>, and document.baseURI would otherwise fall back to
    // the current route URL (e.g. /realtime/BTC), yielding a bogus "/realtime/"
    // base that breaks the WS/asset URLs on every deep route.
    let basePath = null;

    function getBasePath() {
        if (basePath !== null) return basePath;
        const baseEl = document.querySelector("base[href]");
        if (!baseEl) {
            basePath = "/";
            return basePath;
        }
        const p = new URL(baseEl.href, location.href).pathname;
        const last = p.lastIndexOf("/");
        basePath = last < 0 ? "/" : p.slice(0, last + 1);
        return basePath;
    }

    function stripBase(pathname) {
        const b = getBasePath();
        if (b === "/" || !pathname) return pathname;
        if (pathname === b.slice(0, -1) || pathname === b) return "/";
        return pathname.indexOf(b) === 0 ? "/" + pathname.slice(b.length) : pathname;
    }

    function prependBase(url) {
        const b = getBasePath();
        if (b === "/" || typeof url !== "string" || url.charAt(0) !== "/" || url.indexOf(b) === 0) return url;
        return b + url.slice(1);
    }

    // Not a constant: when the server rebuilds this page from a resume record it does so as a NEW
    // session, and tells us by re-stamping data-rask-root on the document it sends back. Treating the
    // id as derived from the document rather than as a value read once at startup means the next
    // reconnect claims the session we actually have.
    let sessionId = root.getAttribute("data-rask-root");

    // The sealed record that lets a server which has never heard of this session rebuild the page
    // anyway — after a restart, a redeploy, or (later) a reconnect routed to another node. Opaque
    // here: it is encrypted and signed by the server, and we only ever hand it back. Mirrored into
    // sessionStorage so a plain F5 resumes too, and keyed by the storage key alone because
    // sessionStorage is already scoped to this tab and origin. It dies with the tab, which is the
    // lifetime we want — a record is state, not a credential, and never outlives the browsing session.
    const RESUME_STORAGE_KEY = "rask.resume";
    let resumeToken = null;
    try {
        resumeToken = sessionStorage.getItem(RESUME_STORAGE_KEY);
    } catch (err) {
        // Storage can throw outright in a partitioned or cookie-blocked context. Resume then works
        // for a reconnect (the token still lives in memory) but not across an F5. Not worth failing over.
    }

    function rememberResumeToken(token) {
        resumeToken = token;
        try {
            sessionStorage.setItem(RESUME_STORAGE_KEY, token);
        } catch (err) {
            // See above — memory-only is a working degradation.
        }
    }

    function forgetResumeToken() {
        resumeToken = null;
        try {
            sessionStorage.removeItem(RESUME_STORAGE_KEY);
        } catch (err) {
        }
    }

    const proto = location.protocol === "https:" ? "wss:" : "ws:";
    const baseWsUrl = proto + "//" + location.host + prependBase("/rask/ws");

    // JWT-on-WebSocket hook. Browsers can't set Authorization headers on a WS upgrade, so a
    // bearer-token app carries the access token on the URL as ?access_token= (the SignalR pattern;
    // pair it with AddJwtBearer's OnMessageReceived reading the query for the Rask WS path). The
    // token is read fresh on every (re)connect from window.Rask.authToken (string or function) or a
    // <meta name="rask-access-token"> tag. With no token set this is a no-op — cookie auth is
    // unaffected and the URL is unchanged.
    function buildWsUrl() {
        let token = null;
        try {
            const r = window.Rask;
            if (r && typeof r.authToken === "function") token = r.authToken();
            else if (r && typeof r.authToken === "string") token = r.authToken;
            if (!token) {
                const meta = document.querySelector('meta[name="rask-access-token"]');
                if (meta) token = meta.getAttribute("content");
            }
        } catch (e) {
            token = null;
        }
        if (!token) return baseWsUrl;
        return baseWsUrl + (baseWsUrl.indexOf("?") >= 0 ? "&" : "?") + "access_token=" + encodeURIComponent(token);
    }

    let ws = null;
    const queue = [];
    let open = false;
    let attempt = 0;
    let reconnectTimer = null;
    let suppressEvents = false;
    const overlay = installOverlay();
    // The reconnect overlay doubles as the auth-handshake indicator. During a sign-in/out the
    // socket is deliberately closed and reconnected to pick up the new cookie; that reconnect is
    // an authentication step, not a dropped connection, so the overlay says "Authenticating…"
    // instead of "Reconnecting…" for its duration.
    const overlayMsg = overlay.querySelector(".rask-overlay__msg");
    let authInProgress = false;
    const RECONNECT_MSG = "Reconnecting…";
    const AUTH_MSG = "Authenticating…";
    // Wait this long after a drop before showing the full-screen overlay, so a sub-second network blip
    // that reconnects fast never flashes the blur + inert freeze over the whole app.
    const OVERLAY_GRACE_MS = 700;
    // After this many failed reconnect attempts (~7.5s of backoff), escalate the overlay from the
    // neutral "Reconnecting…" to a "still trying / you're offline" state with a manual Retry button.
    const ESCALATE_AFTER_ATTEMPTS = 4;
    // Fallback auto-reload delay after the server reports the session is gone (see showSessionExpired).
    const SESSION_EXPIRED_RELOAD_MS = 4000;
    // Dev-only counterpart: under `dotnet watch` an unknown session means the app just restarted for
    // a rude edit, so there is nothing to wait for — get back on screen.
    const DEV_RESTART_RELOAD_MS = 250;
    let overlayTimer = null;
    let sessionExpired = false;

    function setOverlayMessage(text) {
        if (overlayMsg) overlayMsg.textContent = text;
    }

    function setInert(value) {
        if ("inert" in document.body) document.body.inert = value;
    }

    connect();

    function connect() {
        // Single-flight: never open a second socket while one is CONNECTING or OPEN. The online event
        // and the Retry button both funnel here, and during the CONNECTING window `open` is still false,
        // so without this guard they would spawn a duplicate session that double-dispatches every frame.
        if (ws && (ws.readyState === WebSocket.CONNECTING || ws.readyState === WebSocket.OPEN)) {
            return;
        }

        ws = new WebSocket(buildWsUrl());

        ws.addEventListener("open", () => {
            open = true;
            attempt = 0;
            suppressEvents = false;
            // The resume record rides along so a server that doesn't know this session can rebuild the
            // page instead of telling us to reload. A server that DOES know it ignores the field
            // entirely — the intact session is always the better outcome, and is what a normal
            // reconnect within the grace period gets.
            const hello = {type: "hello", session: sessionId};
            if (resumeToken) hello.resume = resumeToken;
            ws.send(JSON.stringify(hello));
            for (const m of queue) ws.send(m);
            queue.length = 0;
            // Auth reconnect completed — restore the default message for any future drop.
            if (authInProgress) {
                authInProgress = false;
                setOverlayMessage(RECONNECT_MSG);
            }
            hideOverlay();
        });

        ws.addEventListener("message", (e) => {
            let data;
            try {
                data = JSON.parse(e.data);
            } catch (err) {
                return;
            }
            // Once the session is known-gone, ignore any late frames still in flight — they would apply
            // against a session the server has already discarded, flashing inconsistent UI before reload.
            if (sessionExpired) {
                return;
            }
            if (data.type === "session" && data.status === "unknown") {
                // The server could not rebuild us — either we carried no record, or it refused the one
                // we had (expired, issued to another user, or sealed under a key ring this host does not
                // have). Drop it: replaying a record the server has already refused just fails again on
                // every future reconnect, and keeps a stale page's state alive across the reload.
                forgetResumeToken();
                showSessionExpired();
                return;
            }
            // A fresh sealed record. Purely something to hold: it changes nothing on screen, and must
            // not fall through to applyFullReply, which would morph the document against a frame that
            // carries no html.
            if (data.type === "resume") {
                if (typeof data.token === "string") rememberResumeToken(data.token);
                return;
            }
            // Dev-only: the coordinator finished applying an edit and every session has repainted.
            // Purely an indicator — the DOM was already updated by the render that preceded this
            // frame, so it must NOT fall through to applyFullReply (which would morph the document
            // against a payload that carries no html).
            if (data.type === "hotReload") {
                if (devMode && data.status === "applied") window.__raskHotReloadPill();
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
                _renderQueue = _renderQueue.then(() => applyDiffReply(data), () => applyDiffReply(data));
                return;
            }
            _renderQueue = _renderQueue.then(() => applyFullReply(data), () => applyFullReply(data));
        });

        ws.addEventListener("close", scheduleReconnect);
        ws.addEventListener("error", scheduleReconnect);
    }

    function scheduleReconnect() {
        if (reconnectTimer !== null || sessionExpired) return;
        open = false;
        resetPending();
        // Freeze interaction immediately (inert) so events during the outage can't queue up and replay
        // as duplicate submits on reconnect. The *visible* blur overlay, by contrast, is debounced: an
        // auth handshake is deliberate (show at once), but an unexpected drop waits OVERLAY_GRACE_MS so a
        // fast reconnect never flashes the modal. connect()'s open handler cancels the pending show.
        setInert(true);
        if (authInProgress) {
            showOverlay();
        } else if (overlayTimer === null && !overlay.hasAttribute("data-show")) {
            overlayTimer = setTimeout(showOverlay, OVERLAY_GRACE_MS);
        }
        const delays = [500, 1000, 2000, 4000, 5000];
        const delay = delays[Math.min(attempt, delays.length - 1)];
        attempt++;
        reconnectTimer = setTimeout(() => {
            reconnectTimer = null;
            connect();
        }, delay);
        updateOverlayState();
    }

    // Reflect the current reconnect state in the (already-visible) overlay: after a few failed attempts,
    // or while the browser reports itself offline, escalate from the neutral spinner message to an
    // explanatory one and reveal a manual "Retry now" button. Left alone during an auth handshake and
    // once the session has expired (both own the overlay message).
    function updateOverlayState() {
        if (authInProgress || sessionExpired) return;
        const offline = ("onLine" in navigator) && !navigator.onLine;
        const escalated = offline || attempt > ESCALATE_AFTER_ATTEMPTS;
        setOverlayMessage(escalated
            ? (offline ? "You're offline — waiting to reconnect…" : "Still trying to reconnect…")
            : RECONNECT_MSG);
        setRetryButton(escalated ? "Retry now" : null);
    }

    // Retry immediately: cancel the backoff wait, reset the attempt counter, and reconnect now.
    function retryNow() {
        if (sessionExpired) {
            location.reload();
            return;
        }
        if (reconnectTimer !== null) {
            clearTimeout(reconnectTimer);
            reconnectTimer = null;
        }
        attempt = 0;
        updateOverlayState();
        connect();
    }

    // The server evicted this session (idle past RaskServerOptions.SessionGracePeriod), so the in-memory
    // UI state is gone and a reload is unavoidable. Warn the user and give them the click instead of
    // yanking the page out from under them mid-action; auto-reload as a fallback after a few seconds.
    function showSessionExpired() {
        sessionExpired = true;
        if (reconnectTimer !== null) {
            clearTimeout(reconnectTimer);
            reconnectTimer = null;
        }
        if (overlayTimer !== null) {
            clearTimeout(overlayTimer);
            overlayTimer = null;
        }
        // Close the dead socket so no further frames arrive (the message handler also drops them via the
        // sessionExpired guard) and the close→scheduleReconnect path early-returns on sessionExpired.
        try {
            if (ws) ws.close(1000, "session-expired");
        } catch (e) {
            // ignore
        }
        showOverlay();
        // Under `dotnet watch` an unknown session is almost always the app having just restarted for
        // an edit hot reload could not apply — the fresh process has no memory of this session id.
        // Say that, and get back on screen quickly. The rare genuine expiry resolves the same way (a
        // reload), just sooner. Production keeps the 4s grace and the accurate wording.
        setOverlayMessage(devMode
            ? "Server restarted — reloading…"
            : "Your session timed out. Reload to continue.");
        setRetryButton("Reload");
        setTimeout(function () { location.reload(); },
            devMode ? DEV_RESTART_RELOAD_MS : SESSION_EXPIRED_RELOAD_MS);
    }

    // Dev-only "hot reload applied" indicator, spliced from Rask.Core/Resources/rask-hotreload.js —
    // the same source WASM and Native use, so the three transports cannot drift. It exposes
    // window.__raskHotReloadPill; only the way the notification *arrives* differs per transport (here,
    // the hotReload frame branch above).
    // @@RASK_HOTRELOAD@@

    // Toggle the overlay's manual action button. Pass a label to show it, or null to hide it. A single
    // click handler routes to retryNow(), which reloads when the session has expired and otherwise
    // forces an immediate reconnect.
    function setRetryButton(label) {
        const btn = overlay.querySelector(".rask-overlay__retry");
        if (!btn) return;
        if (label) {
            btn.textContent = label;
            btn.hidden = false;
        } else {
            btn.hidden = true;
        }
    }

    function installOverlay() {
        // data-rask-managed tells rask-morph.js's diff to treat this node as
        // invisible — these are framework-managed siblings of the server-rendered
        // tree and would otherwise get trimmed on the first morph that doesn't
        // include them. data-rask-overlay is just a query selector tag.
        const style = document.createElement("style");
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
            "@keyframes rask-spin{to{transform:rotate(360deg);}}" +
            ".rask-overlay__retry{margin-left:6px;padding:6px 12px;border:1px solid rgba(255,255,255,.4);" +
            "background:rgba(255,255,255,.12);color:#fff;border-radius:6px;font:inherit;cursor:pointer;}" +
            ".rask-overlay__retry:hover{background:rgba(255,255,255,.24);}" +
            ".rask-overlay__retry[hidden]{display:none;}";
        document.head.appendChild(style);

        const el = document.createElement("div");
        el.className = "rask-overlay";
        el.setAttribute("data-rask-managed", "");
        el.setAttribute("aria-live", "polite");
        el.setAttribute("aria-hidden", "true");
        // Built via DOM APIs rather than innerHTML: the markup is static and
        // framework-owned, but constructing it avoids the only innerHTML write on a
        // non-<template> path, which keeps the runtime clean under a strict CSP / lint.
        const card = document.createElement("div");
        card.className = "rask-overlay__card";
        const spinner = document.createElement("span");
        spinner.className = "rask-overlay__spinner";
        spinner.setAttribute("aria-hidden", "true");
        const msg = document.createElement("span");
        msg.className = "rask-overlay__msg";
        msg.textContent = "Reconnecting…";
        // Manual action button — hidden until the reconnect escalates (Retry now) or the session
        // expires (Reload). One handler routes both via retryNow().
        const retry = document.createElement("button");
        retry.className = "rask-overlay__retry";
        retry.type = "button";
        retry.hidden = true;
        retry.addEventListener("click", function () { retryNow(); });
        card.appendChild(spinner);
        card.appendChild(msg);
        card.appendChild(retry);
        el.appendChild(card);
        document.documentElement.appendChild(el);
        return el;
    }

    function showOverlay() {
        if (overlayTimer !== null) {
            clearTimeout(overlayTimer);
            overlayTimer = null;
        }
        overlay.setAttribute("data-show", "");
        overlay.setAttribute("aria-hidden", "false");
        setInert(true);
        updateOverlayState();
    }

    function hideOverlay() {
        if (overlayTimer !== null) {
            clearTimeout(overlayTimer);
            overlayTimer = null;
        }
        overlay.removeAttribute("data-show");
        overlay.setAttribute("aria-hidden", "true");
        setRetryButton(null);
        setInert(false);
    }

    // A regained network connection should collapse the backoff wait and try to reconnect now, rather
    // than sitting out the remaining delay. It does NOT reset the attempt counter (a flapping connection
    // must keep backing off) and relies on connect()'s single-flight guard so it can't spawn a second
    // socket while one is already in flight. The offline transition just refreshes the overlay copy.
    if (typeof window !== "undefined" && window.addEventListener) {
        window.addEventListener("online", function () {
            if (open || sessionExpired) return;
            if (reconnectTimer !== null) {
                clearTimeout(reconnectTimer);
                reconnectTimer = null;
            }
            connect();
            updateOverlayState();
        });
        window.addEventListener("offline", function () {
            if (overlay.hasAttribute("data-show")) updateOverlayState();
        });
    }

    // Slow-link pending-action indicator. A handler event (click/input/change/submit)
    // is tagged with a monotonic seq; the server replies {type:"ack",seq} once it has
    // processed the dispatch — crucially even when the render dedupes and ships no frame.
    // If no ack lands within PENDING_LATENCY_MS we surface a thin top-of-viewport bar so
    // a high-latency user sees that their action registered; it clears when the matching
    // (or any later) ack arrives. A hard timeout backstops a genuinely lost frame. This
    // is distinct from — and sits one z-index below — the full reconnect overlay above.
    const PENDING_LATENCY_MS = 300;
    const PENDING_HARD_TIMEOUT_MS = 10000;
    let seqCounter = 0;
    let outstandingSeq = 0;
    let ackedSeq = 0;
    let pendingTimer = null;
    let pendingHardTimer = null;
    let pendingVisible = false;
    const pendingBar = installPendingBar();

    // Navigation reuses the same top progress bar as a handler round-trip, tracked separately so a slow
    // route render surfaces progress even though a `navigate` frame carries no handler seq/ack. The bar
    // stays up while EITHER a handler seq or a navigation is outstanding (see hideBarIfIdle).
    let navInFlight = false;
    let navBarTimer = null;
    let navHardTimer = null;
    // Polite live region that announces the new page on a forward navigation (keyboard/SR users get told
    // the route changed; the visible bar alone is silent to assistive tech).
    const routeAnnouncer = installRouteAnnouncer();

    function beginNav() {
        navInFlight = true;
        if (navBarTimer === null && !pendingVisible) {
            navBarTimer = setTimeout(showPendingBar, PENDING_LATENCY_MS);
        }
        // Backstop, mirroring the handler forcePendingTimeout: if no navigation reply ever arrives (the
        // route render threw server-side and sent no frame, or deduped to nothing), settle so the bar
        // can't wedge navigation AND every subsequent handler round-trip.
        if (navHardTimer !== null) {
            clearTimeout(navHardTimer);
        }
        navHardTimer = setTimeout(endNav, PENDING_HARD_TIMEOUT_MS);
    }

    function endNav() {
        navInFlight = false;
        if (navHardTimer !== null) {
            clearTimeout(navHardTimer);
            navHardTimer = null;
        }
        hideBarIfIdle();
    }

    // Retire the progress bar only when nothing needs it — a handler round-trip (ackedSeq < outstandingSeq)
    // or an in-flight navigation keeps it visible.
    function hideBarIfIdle() {
        if (ackedSeq < outstandingSeq || navInFlight) {
            return;
        }

        if (navBarTimer !== null) {
            clearTimeout(navBarTimer);
            navBarTimer = null;
        }

        hidePendingBar();
    }

    function installRouteAnnouncer() {
        const el = document.createElement("div");
        el.setAttribute("data-rask-managed", "");
        el.setAttribute("aria-live", "polite");
        el.setAttribute("aria-atomic", "true");
        el.className = "rask-route-announcer";
        el.style.cssText = "position:absolute;width:1px;height:1px;margin:-1px;padding:0;overflow:hidden;"
            + "clip:rect(0 0 0 0);white-space:nowrap;border:0;";
        document.documentElement.appendChild(el);
        return el;
    }

    // Forward navigation committed: announce the new page (its <title>, morphed in) and move focus into
    // the new page so keyboard/SR users continue from it rather than the now-removed link. `preferred` is
    // the in-page anchor a fragment nav scrolled to (focus it), else the main content is used.
    function focusAndAnnounceRoute(preferred) {
        if (routeAnnouncer) {
            routeAnnouncer.textContent = "";
            const title = document.title || location.pathname;
            // Defer so the reset registers before the new text (some SRs coalesce same-tick changes).
            setTimeout(function () { routeAnnouncer.textContent = title; }, 50);
        }

        const target = preferred
            || document.querySelector("main, [role=main]")
            || document.querySelector("h1");
        if (!target) {
            return;
        }

        // Make it programmatically focusable if it isn't already. Add the blur cleanup only AFTER a
        // successful focus, and back the tabindex out if focus throws, so a detached target can't leak a
        // client-injected tabindex + dangling listener onto server-owned DOM.
        const addedTabindex = !target.hasAttribute("tabindex");
        if (addedTabindex) {
            target.setAttribute("tabindex", "-1");
        }

        let focused = false;
        try {
            target.focus({preventScroll: true});
            focused = true;
        } catch (e) {
            try {
                target.focus();
                focused = true;
            } catch (e2) {
                // focus target vanished
            }
        }

        if (!focused) {
            if (addedTabindex) {
                target.removeAttribute("tabindex");
            }
            return;
        }

        if (addedTabindex) {
            target.addEventListener("blur", function onBlur() {
                target.removeAttribute("tabindex");
                target.removeEventListener("blur", onBlur);
            });
        }
    }

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
        // A navigation may still need the bar even though this handler round-trip settled.
        hideBarIfIdle();
    }

    function resetPending() {
        // On disconnect the reconnect overlay takes over; drop the bar and treat every
        // outstanding handler AND any in-flight navigation as settled so a pre-drop seq/nav
        // can't wedge the next session.
        ackedSeq = outstandingSeq = seqCounter;
        navInFlight = false;
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
        const style = document.createElement("style");
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

        const el = document.createElement("div");
        el.className = "rask-pending";
        el.setAttribute("data-rask-managed", "");
        el.setAttribute("data-rask-pending", "");
        el.setAttribute("aria-hidden", "true");
        const bar = document.createElement("div");
        bar.className = "rask-pending__bar";
        el.appendChild(bar);
        document.documentElement.appendChild(el);
        return el;
    }

    // scopedJsReady starts true: per-component scripts ship as
    // <script src="/_rask/a/{hash}.js" defer> tags in the initial HTML's <head> (and
    // are morphed in/out as components mount/unmount). The browser's defer semantics
    // run them in document order before DOMContentLoaded, which is well before any
    // user click could trigger a Rask.* invoke. The legacy bundle-based gate that
    // waited for a single big script to load is gone with the bundle endpoint itself.
    let scopedJsReady = true;
    let pendingScopedInvokes = [];

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
    const pendingHeadAssets = new Set();
    const trackedHeadAssets = new WeakSet();
    const failedHeadAssets = new Set();
    const HEAD_ASSET_LOAD_TIMEOUT_MS = 5000;

    function isAssetAlreadyLoaded(url) {
        if (!url || !window.performance || !performance.getEntriesByName) return false;
        for (const entry of performance.getEntriesByName(url)) {
            if (entry.responseEnd > 0) return true;
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
        const keyAttr = el.getAttribute("data-rask-key");
        if (keyAttr && keyAttr.indexOf("rsk-") === 0) return;
        let url;
        if (el.tagName === "SCRIPT" && el.src) url = el.src;
        else if (el.tagName === "LINK" && el.rel === "stylesheet" && el.href) url = el.href;
        else return;
        trackedHeadAssets.add(el);
        if (isAssetAlreadyLoaded(url)) return;
        pendingHeadAssets.add(el);
        const finish = (outcome) => {
            if (!pendingHeadAssets.delete(el)) return;
            if (outcome === "error" || outcome === "timeout") {
                failedHeadAssets.add(url);
                const reason = outcome === "error"
                    ? "fired 'error' event (network failure / blocked / integrity mismatch / CSP)"
                    : `did not fire load/error within ${HEAD_ASSET_LOAD_TIMEOUT_MS}ms — proceeding anyway`;
                // console.warn rather than .error: the page CAN still render
                // (the user's defensive code is the contract). Surface enough
                // context that the consequent TypeError in user JS is traceable
                // back to the asset that failed.
                console.warn(`[Rask] Head asset (${el.tagName.toLowerCase()}) ${url} ${reason}. ` +
                    "Queued Rask.* invokes will run; user JS depending on this asset's global must be defensive.");
            }
            maybeDrainPendingInvokes();
        };
        el.addEventListener("load", () => finish("load"), {once: true});
        el.addEventListener("error", () => finish("error"), {once: true});
        // Safety: the load/error event may have fired between insertion and
        // our listener attach (cache hit on a CDN). Performance.getEntriesByName
        // covers the common case; the timeout covers everything else so a
        // missed event doesn't hold Rask.* invokes forever.
        setTimeout(() => finish("timeout"), HEAD_ASSET_LOAD_TIMEOUT_MS);
    }

    function scanHeadAssets() {
        for (const el of document.head.querySelectorAll("script[src], link[rel=stylesheet]")) {
            trackHeadAsset(el);
        }
    }

    function headAssetsReady() {
        return pendingHeadAssets.size === 0;
    }

    // Scoped-CSS FOUC gating: CSS_FOUC_GUARD_MS + waitForUnappliedHeadCss (diff path) +
    // preloadNewHeadStylesheets (full-HTML path) — spliced from Rask.Core/Resources/rask-scoped.js,
    // shared with rask.wasm.js + rask.native.js.
    // @@RASK_SCOPED@@

    // Reset scroll on forward navigation only (history.action "push" — a nav-link click
    // or Navigator.Navigate). "replace" (Back/Forward popstate, SetQuery, auth redirect)
    // is left to the browser's native scroll restoration. When the intercepted link
    // carried a "#fragment" matching an element, scroll there instead of the top.
    // Call this only after the new body has committed so the anchor target exists.
    function applyNavScroll(history) {
        // A navigation reply committed (push or replace) — retire the loading bar. Only when `history`
        // is present: an out-of-band / non-nav frame (no history) must NOT clear an in-flight nav's bar.
        if (history && navInFlight) {
            endNav();
        }

        if (!history || history.action === "replace") {
            _pendingScrollHash = "";
            return;
        }
        const hash = _pendingScrollHash;
        _pendingScrollHash = "";
        let anchor = null;
        if (hash && hash.length > 1) {
            try {
                anchor = document.querySelector(hash) ||
                    document.getElementById(decodeURIComponent(hash.slice(1)));
            } catch (e) {
                anchor = null;
            }
        }

        if (anchor) {
            anchor.scrollIntoView();
        } else {
            window.scrollTo(0, 0);
        }
        // Forward navigation: focus the anchor the link targeted (else the main content) and announce the
        // route — a fragment deep-link is still a route change for keyboard/SR users.
        focusAndAnnounceRoute(anchor);
    }

    // Diff-mode render application (wire format matches LivePayload.BuildPayloadUtf8Diff).
    // The head rides the payload as a <head> fragment (user Head contributions are collected
    // + spliced server-side, so they're not in the frame stream). Morph the head FIRST —
    // keyed reconciliation (data-rask-key) keeps unchanged scoped-CSS links — and when it
    // adds a not-yet-applied scoped stylesheet, defer the body ops until it applies so the
    // swapped body never paints unstyled (FOUC). Returns the wait Promise so _renderQueue
    // holds the next message until the body has committed.
    function applyDiffReply(data) {
        const applyBody = () => {
            // Each op carries a Path (childNodes indices from the document root) and an
            // op-specific payload.
            applyDiff(data.ops, Array.isArray(data.names) ? data.names : null);
            if (data.history && typeof data.history.url === "string") {
                let diffTarget = prependBase(data.history.url);
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
            const freshHead = new DOMParser().parseFromString(data.head, "text/html").head;
            if (freshHead) {
                morph(document.head, freshHead);
                const wait = waitForUnappliedHeadCss();
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
        let freshHtml = null;
        if (typeof data.html === "string") {
            const doc = new DOMParser().parseFromString(data.html, "text/html");
            freshHtml = doc.documentElement;
        }

        const commit = () => {
            if (freshHtml) {
                morph(document.documentElement, freshHtml);
                root = document.querySelector("[data-rask-root]") || root;
                // Every full frame re-stamps data-rask-root, so this is where a rebuilt session's NEW
                // id arrives — the server answered our hello by building a fresh session around the
                // resume record rather than by finding the one we asked for. Following the document
                // keeps the next reconnect pointed at the session we actually have; without it we would
                // keep claiming an id no server will ever know again.
                const stamped = root && root.getAttribute("data-rask-root");
                if (stamped) sessionId = stamped;
                // Pick up any newly-inserted Head-declared external assets (e.g., a
                // page-specific Script in Component.Head) so their load events feed the gate.
                scanHeadAssets();
            }
            if (data.history && typeof data.history.url === "string") {
                let fullTarget = prependBase(data.history.url);
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
            const wait = preloadNewHeadStylesheets(freshHtml);
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
        const stillWaiting = [];
        const ready = [];
        for (const inv of pendingScopedInvokes) {
            if (raskNamespaceReady(inv.identifier)) ready.push(inv);
            else stillWaiting.push(inv);
        }
        pendingScopedInvokes = stillWaiting;
        for (const inv of ready) dispatchJsInvoke(inv);
    }

    function raskNamespaceReady(identifier) {
        if (typeof identifier !== "string") return true;
        if (identifier.indexOf("Rask.") !== 0) return true;
        const rest = identifier.substring(5);
        const dot = rest.indexOf(".");
        const name = dot < 0 ? rest : rest.substring(0, dot);
        return !!(window.Rask && window.Rask[name]);
    }

    // Per-component scripts load asynchronously from /_rask/a/{hash}.js. A first-render
    // Rask.* invoke races their load; the parked invoke wakes when window.Rask.{TypeName}
    // appears (or after the 5s timeout, in which case the original "Could not find" surfaces).
    const RASK_NS_POLL_INTERVAL_MS = 100;
    const RASK_NS_POLL_TIMEOUT_MS = 5000;
    let raskNsPollHandle = 0;
    let raskNsPollStarted = 0;

    function ensureRaskNamespacePoll() {
        if (raskNsPollHandle !== 0) return;
        raskNsPollStarted = Date.now();
        raskNsPollHandle = setInterval(() => {
            if (pendingScopedInvokes.length === 0
                || Date.now() - raskNsPollStarted > RASK_NS_POLL_TIMEOUT_MS) {
                clearInterval(raskNsPollHandle);
                raskNsPollHandle = 0;
                const drained = pendingScopedInvokes;
                pendingScopedInvokes = [];
                for (const inv of drained) {
                    // After timeout, force-dispatch through the post-gate body so the
                    // original "Could not find" surface (caught by the user's ErrorBoundary)
                    // beats hanging forever on a broken asset URL.
                    forceDispatchJsInvoke(inv);
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
        const msg = JSON.stringify(payload);
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
        }).then(() => {
            try {
                if (ws) ws.close(1000, "auth-refresh");
            } catch (e) {
            }
        }).catch((err) => {
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

    document.addEventListener("click", (e) => {
        if (e.defaultPrevented) return;
        if (e.button !== 0 || e.metaKey || e.ctrlKey || e.shiftKey || e.altKey) return;
        const a = e.target.closest("a[data-rask-nav]");
        if (!a) return;
        if (a.getAttribute("target") === "_blank") return;
        const href = a.getAttribute("href");
        if (!href) return;
        let url;
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
        beginNav();
        send({type: "navigate", path: stripBase(url.pathname), query: url.search});
    });

    window.addEventListener("popstate", () => {
        flushInputsNow();
        beginNav();
        send({type: "navigate", path: stripBase(location.pathname), query: location.search, replace: true});
    });

    document.addEventListener("click", (e) => {
        const t = e.target.closest("[data-rask-on-click]");
        if (!t || !inRoot(t)) return;
        // A submit/reset button is driven by native form submission (handled by the dedicated submit
        // listener). Don't let an ANCESTOR click handler (e.g. a modal's .modal-dialog shield) hijack it
        // and cancel the default — that would break the form submit. A handler on the button itself
        // still runs: note `button.type` defaults to "submit" for a bare <button>, so gating on the
        // ancestor (t !== btn) is what keeps a plain Button(OnClick:) working here.
        const btn = e.target.closest("button, input");
        if (btn && btn !== t && (btn.type === "submit" || btn.type === "reset")) return;
        e.preventDefault();
        flushInputsNow();
        send({
            id: t.getAttribute("data-rask-on-click"), type: "click",
            shiftKey: e.shiftKey, ctrlKey: e.ctrlKey, altKey: e.altKey, metaKey: e.metaKey
        });
    });

    // rAF-coalesced input & scroll dispatch (inputPending/flushInputsNow/queueInput + the input and
    // scroll listeners) — spliced from Rask.Core/Resources/rask-input.js, shared with rask.wasm.js +
    // rask.native.js. MUST precede @@RASK_EVENTS@@ (its keyboard handler calls flushInputsNow).
    // @@RASK_INPUT@@

    document.addEventListener("change", (e) => {
        const t = e.target.closest("[data-rask-on-change], [data-rask-on-files]");
        if (!t || !inRoot(t)) return;
        // Flush before processing — if the same element (or a sibling) has a pending
        // coalesced input, the server needs to see it BEFORE the change-triggered
        // validator / handler runs, otherwise the validator reads stale model state.
        flushInputsNow();
        if (t.tagName === "INPUT" && t.type === "file" && t.hasAttribute("data-rask-on-files")) {
            const files = t.files;
            if (!files || files.length === 0) return;
            uploadFiles(files).then((metas) => {
                send({id: t.getAttribute("data-rask-on-files"), type: "files", files: metas});
            }).catch((err) => {
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
            const changeVal = (t.tagName === "INPUT" && t.type === "checkbox")
                ? (t.checked ? "true" : "false")
                : t.value;
            // Record the PRE-EDIT value (the last server-rendered `value` attribute)
            // so a lagging re-render carrying that stale value can't clobber the
            // user's fresh edit before the server's authoritative response lands —
            // see raskShouldSuppressValue. Checkboxes self-correct via the checked
            // path, so they don't participate in the value guard.
            if (!(t.tagName === "INPUT" && t.type === "checkbox")) {
                const sv = t.getAttribute("value");
                raskNotePendingValue(t, sv === null ? "" : sv);
            }
            // Same guard for the `.checked` property: record the PRE-CLICK checked (the `checked`
            // attribute, which a native click leaves untouched) so a lagging re-render can't revert
            // the just-committed selection before the server echoes it — see raskShouldSuppressChecked.
            // For a radio, note the whole same-name group: a stale frame that re-checks the previously
            // selected radio would natively uncheck the new one, so the siblings need the guard too.
            if (t.tagName === "INPUT" && (t.type === "checkbox" || t.type === "radio")) {
                if (t.type === "radio" && t.name) {
                    root.querySelectorAll('input[type=radio][name="' + CSS.escape(t.name) + '"]')
                        .forEach((r) => raskNotePendingChecked(r, r.hasAttribute("checked")));
                } else {
                    raskNotePendingChecked(t, t.hasAttribute("checked"));
                }
            }
            send({id: t.getAttribute("data-rask-on-change"), type: "change", value: changeVal});
        }
    });

    function uploadFiles(files) {
        const fd = new FormData();
        for (let i = 0; i < files.length; i++) {
            fd.append(`f${i}`, files[i], files[i].name);
            fd.append(`f${i}__lastModified`, String(files[i].lastModified || 0));
        }
        return fetch(prependBase(`/_rask/upload/${encodeURIComponent(sessionId)}`), {
            method: "POST",
            body: fd,
            credentials: "same-origin"
        }).then((res) => {
            if (!res.ok) throw new Error(`upload failed: ${res.status}`);
            return res.json();
        }).then((json) => Array.isArray(json.files) ? json.files : []);
    }

    function triggerDownload(url, filename) {
        // url is framework-built (/_rask/download/...); resolve + reject anything
        // that isn't same-origin so a javascript:/cross-origin href can never land here.
        let resolved;
        try {
            resolved = new URL(url, location.href);
        } catch (_) {
            return;
        }
        if (resolved.origin !== location.origin) return;
        const a = document.createElement("a");
        a.href = resolved.href;
        a.download = filename;
        a.style.display = "none";
        document.body.appendChild(a);
        a.click();
        setTimeout(() => {
            try {
                document.body.removeChild(a);
            } catch (_) {
            }
        }, 0);
    }

    document.addEventListener("submit", (e) => {
        const t = e.target.closest("[data-rask-on-submit]");
        if (!t || !inRoot(t)) return;
        e.preventDefault();
        flushInputsNow();
        submitForm(t).catch((err) => {
            console.error("Rask: submit failed", err);
        });
    });

    function submitForm(form) {
        const obj = {};
        const fileInputs = form.querySelectorAll('input[type="file"][name]');
        const pending = [];
        const fileFields = {};
        for (const input of fileInputs) {
            if (!input.files || input.files.length === 0) continue;
            pending.push(uploadFiles(input.files).then((metas) => {
                fileFields[input.name] = metas;
            }));
        }
        return Promise.all(pending).then(() => {
            const fd = new FormData(form);
            fd.forEach((v, k) => {
                if (v instanceof File || v instanceof Blob) return;
                obj[k] = String(v);
            });
            if (Object.keys(fileFields).length > 0) obj.__files = fileFields;
            send({id: form.getAttribute("data-rask-on-submit"), type: "submit", form: obj});
        });
    }

    // Extended GlobalEventHandlers delegation + keyboard (keydown/keyup) + the four core drag events
    // (dragstart/dragover/drop/dragend) — spliced from Rask.Core/Resources/rask-events.js.
    // @@RASK_EVENTS@@

    // ----- IJSRuntime global-JS dispatcher -----------------------------------
    // Mirrors the Microsoft.JSInterop contract: server sends an "identifier" like
    // "sessionStorage.getItem", we resolve it on window, invoke it with args, then
    // ship a jsResult back keyed by the server-assigned taskId. JSObjectReference
    // returns get a stable handle id; DotNetObjectReference values flow back via a
    // {__dotNetObject:<id>} placeholder so the .NET side can re-hydrate them.

    const jsObjectRefs = new Map();   // id -> target
    let nextJsObjectRefId = 1;

    function resolveIdentifier(target, identifier) {
        // Walk a dotted JS path on the given target (typically window). Returns
        // [parentObject, lastSegment] so the caller can preserve `this` when
        // calling methods (e.g. sessionStorage.setItem must run with sessionStorage
        // as `this`). Returns null on miss — caller throws.
        if (typeof identifier !== "string" || identifier.length === 0) return null;
        const parts = identifier.split(".");
        let parent = target;
        for (let i = 0; i < parts.length - 1; i++) {
            if (parent == null) return null;
            parent = parent[parts[i]];
        }
        if (parent == null) return null;
        const last = parts[parts.length - 1];
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
        const taskId = inv.id;
        const resultType = (typeof inv.resultType === "number") ? inv.resultType : 0;
        const argsJson = (typeof inv.argsJson === "string") ? inv.argsJson : "[]";
        const targetInstanceId = (typeof inv.targetInstanceId === "number") ? inv.targetInstanceId : 0;

        Promise.resolve().then(() => {
            let args;
            try {
                args = JSON.parse(argsJson, jsonReviver);
            } catch (e) {
                throw new Error(`Failed to parse argsJson: ${e.message}`);
            }

            let target = window;
            if (targetInstanceId !== 0) {
                target = jsObjectRefs.get(targetInstanceId);
                if (!target) throw new Error(`Unknown JS object reference: ${targetInstanceId}`);
            }

            const resolved = resolveIdentifier(target, inv.identifier);
            if (!resolved) throw new Error(`Could not find '${inv.identifier}' on target`);
            const parent = resolved[0];
            const key = resolved[1];
            const fn = parent[key];

            // Identifier names a property (not a method) — return its value. This is
            // how blazor handles e.g. `localStorage.length`.
            return (typeof fn === "function") ? fn.apply(parent, args) : fn;
        }).then((value) => {
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
                const refId = nextJsObjectRefId++;
                jsObjectRefs.set(refId, value);
                sendJsResult(taskId, true, {"__jsObjectId": refId});
                return;
            }
            sendJsResult(taskId, true, value);
        }).catch((err) => {
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
            // CSS.escape the id so a value carrying a quote/bracket can't break out of the
            // attribute selector or match an unintended element (defense-in-depth — ids are
            // framework-minted, but the reviver runs on server-supplied JSON).
            if (typeof value.__raskRef__ === "string") {
                return document.querySelector(`[data-rask-ref="${CSS.escape(value.__raskRef__)}"]`);
            }
        }
        return value;
    }

    function sendJsResult(id, success, result, error) {
        const msg = {type: "jsResult", id, success};
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
    const dotNetPending = new Map();    // callId -> {resolve, reject}
    let nextDotNetCallId = 1;

    window.DotNet = window.DotNet || {
        invokeMethodAsync(assemblyName, methodIdentifier, ...args) {
            const callId = String(nextDotNetCallId++);
            return new Promise((resolve, reject) => {
                dotNetPending.set(callId, {resolve, reject});
                send({
                    type: "dotNetInvoke",
                    callId,
                    assemblyName,
                    methodIdentifier,
                    argsJson: JSON.stringify(args)
                });
            });
        },
        _endInvokeDotNet(msg) {
            const pending = dotNetPending.get(msg.callId);
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
