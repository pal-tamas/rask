// The "Install this app" prompt — beforeinstallprompt.
//
// See ./geolocation.ts for the two rules every module in this directory follows, and note the
// exception this module is FORCED into: it has to listen for `beforeinstallprompt` before the browser
// fires it, and the browser fires it once, early. So `listen()` is exported for a host to call at
// boot, rather than the listeners being attached at import — which keeps the import side-effect free
// while still catching the event.
//
// Deliberately NOT preventDefault() on the event: doing so suppresses the browser's own install
// affordance for the whole app, and it buys nothing — the mini-infobar it used to suppress was
// removed in Chrome 76, and a deferred event replays fine without it.

let deferred: BeforeInstallPromptEventLike | null = null;
let installed = false;
let listening = false;

/** Start capturing the install event. Idempotent; call it once at boot. */
export function listen(): void {
    if (listening || typeof window === "undefined") {
        return;
    }
    listening = true;
    window.addEventListener("beforeinstallprompt", (e) => {
        deferred = e as BeforeInstallPromptEventLike;
    });
    window.addEventListener("appinstalled", () => {
        installed = true;
        deferred = null;
    });
}

/** Whether a prompt is available to show right now. */
export function canInstall(): boolean {
    return deferred != null;
}

/**
 * Whether the app is already installed — either we saw it happen, or we are running in a standalone
 * window, which is the only signal available on a fresh launch.
 */
export function isInstalled(): boolean {
    if (installed) {
        return true;
    }
    if (typeof window === "undefined") {
        return false;
    }
    return !!(window.matchMedia && window.matchMedia("(display-mode: standalone)").matches)
        || window.navigator.standalone === true;
}

/**
 * Show the prompt. Resolves "accepted", "dismissed", or "unavailable" when there was no deferred
 * event to replay.
 *
 * Must be called from a user gesture, and the event is spent either way — a dismissed prompt cannot
 * be shown again until the browser decides to offer another one.
 */
export async function prompt(): Promise<string> {
    if (!deferred) {
        return "unavailable";
    }
    deferred.prompt();
    let outcome = "dismissed";
    try {
        const choice = await deferred.userChoice;
        outcome = (choice && choice.outcome === "accepted") ? "accepted" : "dismissed";
    } catch (_) {
        outcome = "dismissed";
    }
    deferred = null;
    return outcome;
}
