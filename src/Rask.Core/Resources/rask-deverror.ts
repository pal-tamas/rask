// Development error overlay — the panel that says what broke, *over* the running app.
//
// Spliced (at "// @@RASK_DEVERROR@@") into the Server runtime (rask.js) and the WASM runtime
// (rask.wasm.js), so both hosts show the same thing. Written in the Server dialect (var/function, no
// arrows, no template literals) because that file is ES5; the WASM module is a superset, so one source
// serves both.
//
// WHY A SECOND OVERLAY RATHER THAN THE RECONNECT ONE. They mean opposite things. The reconnect overlay
// blocks: it makes the document inert because nothing you click can reach a server that isn't there.
// This one must NOT block — the whole point is that the app is still running and still yours to poke at,
// which is what makes it useful for finding the bug. So it is a corner panel with pointer events only on
// itself, and the app behind it stays live (#607).
//
// Never shown outside development. The server only ever puts `devError` on a payload in development
// (DevErrorInfo.From returns null otherwise), and the client checks the document flag as well — two
// independent gates, because this renders a stack trace.

var devErrorPanel: HTMLElement | null = null;
var devErrorCount = 0;
var devErrorLastLogged = "";

// The dev gate, read from the document rather than from a host-local variable, so this one source works
// in both runtimes. The Server stamps `data-rask-dev` onto <body> per request (LivePayload
// .InjectRootAttr); the WASM runtime sets it at boot from its host's own answer, because it renders
// client-side and no server ever touches its <body>.
export function devErrorEnabled() {
    var b = document.body;
    return !!(b && b.hasAttribute("data-rask-dev"));
}

function installDevErrorStyles() {
    var style = document.createElement("style");
    style.setAttribute("data-rask-managed", "");
    style.setAttribute("data-rask-dev-error-style", "");
    style.textContent =
        ".rask-deverr{position:fixed;left:12px;right:12px;bottom:12px;max-width:760px;margin:0 auto;" +
        "z-index:2147483646;font:13px/1.5 ui-monospace,SFMono-Regular,Menlo,Consolas,monospace;" +
        "background:#1b1114;color:#ffe9ec;border:1px solid #7f1d2b;border-radius:10px;" +
        "box-shadow:0 12px 40px rgba(0,0,0,.45);overflow:hidden;}" +
        ".rask-deverr[hidden]{display:none;}" +
        ".rask-deverr__bar{display:flex;align-items:center;gap:10px;padding:10px 12px;" +
        "background:#7f1d2b;color:#fff;}" +
        ".rask-deverr__kind{font-weight:600;letter-spacing:.02em;}" +
        ".rask-deverr__count{background:rgba(255,255,255,.22);border-radius:999px;padding:1px 8px;" +
        "font-size:11px;}" +
        ".rask-deverr__spacer{flex:1;}" +
        ".rask-deverr__btn{border:1px solid rgba(255,255,255,.45);background:rgba(255,255,255,.12);" +
        "color:#fff;border-radius:6px;padding:3px 10px;font:inherit;font-size:12px;cursor:pointer;}" +
        ".rask-deverr__btn:hover{background:rgba(255,255,255,.24);}" +
        ".rask-deverr__body{padding:12px;}" +
        ".rask-deverr__title{font-weight:600;margin:0 0 4px;}" +
        ".rask-deverr__msg{margin:0 0 10px;white-space:pre-wrap;word-break:break-word;}" +
        ".rask-deverr__detail{margin:0;max-height:38vh;overflow:auto;white-space:pre-wrap;" +
        "word-break:break-word;font-size:12px;color:#f0b8c0;background:#140c0e;border-radius:6px;" +
        "padding:10px;}" +
        ".rask-deverr__detail[hidden]{display:none;}";
    document.head.appendChild(style);
}

function installDevErrorPanel() {
    installDevErrorStyles();

    var el = document.createElement("div");
    el.className = "rask-deverr";
    // data-rask-managed keeps the morph from trimming a node the server never rendered — the same
    // contract the reconnect overlay relies on.
    el.setAttribute("data-rask-managed", "");
    el.setAttribute("data-rask-dev-error", "");
    el.setAttribute("role", "alert");
    el.hidden = true;

    var bar = document.createElement("div");
    bar.className = "rask-deverr__bar";

    var kind = document.createElement("span");
    kind.className = "rask-deverr__kind";

    var count = document.createElement("span");
    count.className = "rask-deverr__count";
    count.hidden = true;

    var spacer = document.createElement("span");
    spacer.className = "rask-deverr__spacer";

    var toggle = document.createElement("button");
    toggle.className = "rask-deverr__btn";
    toggle.type = "button";
    toggle.textContent = "Stack";

    var dismiss = document.createElement("button");
    dismiss.className = "rask-deverr__btn";
    dismiss.type = "button";
    dismiss.textContent = "Dismiss";

    var body = document.createElement("div");
    body.className = "rask-deverr__body";

    var title = document.createElement("p");
    title.className = "rask-deverr__title";

    var msg = document.createElement("p");
    msg.className = "rask-deverr__msg";

    var detail = document.createElement("pre");
    detail.className = "rask-deverr__detail";
    detail.hidden = true;

    toggle.addEventListener("click", function () {
        detail.hidden = !detail.hidden;
        toggle.textContent = detail.hidden ? "Stack" : "Hide stack";
    });
    dismiss.addEventListener("click", function () { hideDevError(); });

    bar.appendChild(kind);
    bar.appendChild(count);
    bar.appendChild(spacer);
    bar.appendChild(toggle);
    bar.appendChild(dismiss);
    body.appendChild(title);
    body.appendChild(msg);
    body.appendChild(detail);
    el.appendChild(bar);
    el.appendChild(body);
    document.documentElement.appendChild(el);
    return el;
}

/** The payload's devError object. */
interface DevErrorInfo {
    kind?: string;
    title?: string;
    message?: string;
    detail?: string;
}

/** What the dev-status endpoint reports. Only `state` and the error fields are read here. */
interface DevStatus {
    state?: string;
    title?: string;
    message?: string;
    detail?: string;
}

/** One part of the panel, or null when the panel was built without it. */
function part(panel: HTMLElement, selector: string): HTMLElement | null {
    return panel.querySelector(selector);
}

/** Sets a panel part's text, if the panel has that part. */
function setPart(panel: HTMLElement, selector: string, text: string): void {
    const el = part(panel, selector);
    if (el) el.textContent = text;
}

function devErrorHeading(kind: string | undefined): string {
    if (kind === "handler") return "Unhandled exception in an event handler";
    if (kind === "lifecycle") return "Unhandled exception in an async lifecycle hook";
    if (kind === "build") return "Build failed";
    return "Unhandled exception";
}

// Shows (or updates) the panel. `info` is the payload's devError object: {kind,title,message,detail}.
export function showDevError(info: DevErrorInfo | null | undefined): void {
    if (!devErrorEnabled() || !info || typeof info !== "object") return;
    if (!devErrorPanel) devErrorPanel = installDevErrorPanel();

    // Bound after the install above, so the reads below do not each have to re-prove the panel exists.
    const panel = devErrorPanel;

    // A build failure is one condition being re-reported while it persists (the poll below repeats every
    // 700ms), not a second thing going wrong — so it replaces in place. App faults are genuinely separate
    // events and do count, which is how you notice a handler throwing on every click.
    var countEl = part(panel, ".rask-deverr__count");
    if (info.kind === "build") {
        devErrorCount = 0;
        if (countEl) countEl.hidden = true;
    } else {
        devErrorCount++;
        if (countEl) {
            countEl.textContent = String(devErrorCount);
            countEl.hidden = devErrorCount < 2;
        }
    }

    setPart(panel, ".rask-deverr__kind", devErrorHeading(info.kind));
    setPart(panel, ".rask-deverr__title", info.title || "");
    setPart(panel, ".rask-deverr__msg", info.message || "");

    var detail = part(panel, ".rask-deverr__detail");
    if (detail) {
        detail.textContent = info.detail || "";
        // Collapsed on arrival: the message is what you read first, and a stack that opened itself
        // would cover the app this panel exists to keep visible.
        detail.hidden = true;
    }
    setPart(panel, ".rask-deverr__btn", "Stack");

    panel.hidden = false;

    // Also to the console, where a developer's own breakpoints and filters already live — and where it
    // survives dismissing the panel. Deduped on content, because the build poll re-reports the same
    // failure every 700ms and a console filling with one repeated error is a console nobody reads.
    var signature = info.kind + " " + (info.title || "") + " " + (info.message || "");
    if (signature !== devErrorLastLogged && typeof console !== "undefined" && console.error) {
        devErrorLastLogged = signature;
        console.error("[Rask] " + devErrorHeading(info.kind) + ": " + (info.title || "") +
            (info.message ? ": " + info.message : ""), info.detail || "");
    }
}

export function hideDevError() {
    if (!devErrorPanel) return;
    devErrorPanel.hidden = true;
    devErrorCount = 0;
    var count = part(devErrorPanel, ".rask-deverr__count");
    if (count) count.hidden = true;
    devErrorLastLogged = "";
}

// ---- build status (#603) ----
//
// When the socket drops, the client cannot tell "the server is restarting" from "the server is gone"
// from "the code no longer compiles" — so it assumed the middle one and said "Reconnecting…", then
// escalated to a "Retry now" button that could never succeed. A compile problem reported as a network
// problem, with an action that cannot help.
//
// Nothing inside the app can correct that, because the app is what died. `rask dev` stamps the URL of a
// status endpoint IT owns onto the page (data-rask-dev-status); the client kept that from the last page
// it loaded, so it can still ask after the server is gone. See DevStatusServer.

var devStatusUrl: string | null = null;
var devStatusPolling = false;
var devStatusShowing = false;

function devStatusEndpoint() {
    if (devStatusUrl !== null) return devStatusUrl;
    var b = document.body;
    devStatusUrl = (b && b.getAttribute("data-rask-dev-status")) || "";
    return devStatusUrl;
}

// Asks once. Resolves to the parsed status, or null when there is nobody to ask (production, or a
// `rask dev` too old to serve it) or the endpoint itself is unreachable.
function fetchDevStatus() {
    var url = devStatusEndpoint();
    if (!url || typeof fetch !== "function") return Promise.resolve(null);
    return fetch(url, { cache: "no-store", mode: "cors" })
        .then(function (r) { return r.ok ? r.json() : null; })
        .catch(function () { return null; });
}

// Polls while the build is broken. Shows the compiler errors instead of the reconnect overlay, and
// stops as soon as the code compiles again — at which point the ordinary reconnect takes over and the
// app comes back on its own.
//
// `onResolved` is called when the build is no longer failing, so the caller can put the reconnect
// overlay back if it had been suppressed.
export function pollDevStatus(onFailed: () => void, onResolved: (status: DevStatus | null) => void): void {
    if (devStatusPolling) return;
    devStatusPolling = true;

    var tick = function () {
        fetchDevStatus().then(function (status) {
            if (!status || status.state !== "failed") {
                devStatusPolling = false;
                if (devStatusShowing) {
                    devStatusShowing = false;
                    hideDevError();
                }
                if (onResolved) onResolved(status);
                return;
            }

            if (!devStatusShowing) {
                devStatusShowing = true;
                if (onFailed) onFailed();
            }

            showDevError({
                kind: "build",
                title: status.count > 1 ? status.count + " build errors" : "1 build error",
                message: status.message || "",
                detail: status.detail || ""
            });

            setTimeout(tick, 700);
        });
    };

    tick();
}
