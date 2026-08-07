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

var devErrorPanel = null;
var devErrorCount = 0;

// The dev gate, read from the document rather than from a host-local variable, so this one source works
// in all three runtimes. The Server stamps `data-rask-dev` onto <body> per request (LivePayload
// .InjectRootAttr); the WASM and Native runtimes set it at boot from their host's own answer, because
// they render client-side and no server ever touches their <body>.
function devErrorEnabled() {
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

function devErrorHeading(kind) {
    if (kind === "handler") return "Unhandled exception in an event handler";
    if (kind === "lifecycle") return "Unhandled exception in an async lifecycle hook";
    if (kind === "build") return "Build failed";
    return "Unhandled exception";
}

// Shows (or updates) the panel. `info` is the payload's devError object: {kind,title,message,detail}.
function showDevError(info) {
    if (!devErrorEnabled() || !info || typeof info !== "object") return;
    if (!devErrorPanel) devErrorPanel = installDevErrorPanel();

    devErrorCount++;
    var countEl = devErrorPanel.querySelector(".rask-deverr__count");
    countEl.textContent = String(devErrorCount);
    countEl.hidden = devErrorCount < 2;

    devErrorPanel.querySelector(".rask-deverr__kind").textContent = devErrorHeading(info.kind);
    devErrorPanel.querySelector(".rask-deverr__title").textContent = info.title || "";
    devErrorPanel.querySelector(".rask-deverr__msg").textContent = info.message || "";

    var detail = devErrorPanel.querySelector(".rask-deverr__detail");
    detail.textContent = info.detail || "";
    // Collapsed on arrival: the message is what you read first, and a stack that opened itself would
    // cover the app this panel exists to keep visible.
    detail.hidden = true;
    devErrorPanel.querySelector(".rask-deverr__btn").textContent = "Stack";

    devErrorPanel.hidden = false;

    // Also to the console, where a developer's own breakpoints and filters already live — and where it
    // survives dismissing the panel.
    if (typeof console !== "undefined" && console.error) {
        console.error("[Rask] " + devErrorHeading(info.kind) + ": " + (info.title || "") +
            (info.message ? ": " + info.message : ""), info.detail || "");
    }
}

function hideDevError() {
    if (!devErrorPanel) return;
    devErrorPanel.hidden = true;
    devErrorCount = 0;
    devErrorPanel.querySelector(".rask-deverr__count").hidden = true;
}
