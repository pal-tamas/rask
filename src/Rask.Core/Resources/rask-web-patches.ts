// Rask.Web's browser-quirk patches. Rask.Web runs MDN's members as the browser has them (rask-api.ts's raskWebWalk and
// __raskWeb.listen); where a browser falls short of what MDN describes, the fix is here, by MDN name, and nowhere else.
// Each one names the quirk and the browsers that have it. Nothing in this file runs at import: rask-api.ts arms the one
// that has to listen from boot (raskWebArm).

// ---- SpeechRecognition: a prefixed global --------------------------------------------------------------------------
// Chromium before 139 and Safari ship the Web Speech recogniser only as webkitSpeechRecognition. A global missing under
// its MDN name is looked up under the name the browser gave it: `new SpeechRecognition()`, and the `"SpeechRecognition"
// in window` that IsSupported asks.
const prefixed: Record<string, string> = {
    SpeechRecognition: "webkitSpeechRecognition",
};

/** The name a global goes by in this browser: its MDN name, or the prefixed one it ships under instead. */
export function raskWebGlobalName(owner: object, name: string): string {
    const alias = owner === globalThis && !(name in owner) ? prefixed[name] : undefined;
    return alias !== undefined && alias in owner ? alias : name;
}

// ---- WakeLock.request: a screen lock that survives the page being hidden -------------------------------------------
// Every browser (Chromium, Firefox, Safari — the spec says so) releases a screen wake lock when the page is hidden, and
// never takes it back. What WakeLock.request() answers with here is Rask's own sentinel holding the browser's: it asks
// for the lock again each time the page is visible, until it is released. So it is released once, as MDN's is —
// `released` turns true and `release` fires — when release() is called, or when the browser refuses it back.
class RaskWakeLockSentinel extends EventTarget {
    onrelease: ((event: Event) => void) | null = null;
    private ended = false;

    constructor(private readonly lock: WakeLock, readonly type: WakeLockType, private held: WakeLockSentinel) {
        super();
        document.addEventListener("visibilitychange", this.visible);
    }

    get released(): boolean {
        return this.ended;
    }

    async release(): Promise<void> {
        if (this.ended) return;
        this.end();
        await this.held.release();
    }

    private readonly visible = async (): Promise<void> => {
        if (document.visibilityState !== "visible" || this.ended || !this.held.released) return;
        try {
            const again = await this.lock.request(this.type);
            if (this.ended) await again.release(); // released while the request was in flight
            else this.held = again;
        } catch {
            this.end(); // refused (a battery saver, a revoked permission): the lock is gone for good
        }
    };

    private end(): void {
        this.ended = true;
        document.removeEventListener("visibilitychange", this.visible);
        const event = new Event("release");
        this.dispatchEvent(event);
        this.onrelease?.(event);
    }
}

async function requestWakeLock(lock: WakeLock, args: unknown[]): Promise<RaskWakeLockSentinel> {
    const type = (args[0] as WakeLockType | undefined) ?? "screen";
    return new RaskWakeLockSentinel(lock, type, await lock.request(type));
}

// ---- beforeinstallprompt: an event that fires before anyone listens ------------------------------------------------
// Chromium fires beforeinstallprompt once, as the page loads — before any component has subscribed. The runtime keeps it
// from boot, and hands it to each Window.OnBeforeInstallPrompt subscriber that comes after it, until it is spent:
// prompt() shows it once (BeforeInstallPromptEvent.prompt below, or Trigger.Install), and appinstalled means there is
// nothing left to offer. Not preventDefault()ed: Chromium has shown no mini-infobar to suppress since 76, and
// cancelling it would hide the browser's own install button for every Rask app.
//
// And prompt() itself: Chromium answers with `{outcome, platform}` where MDN's PromptResponseObject is `{userChoice}`
// (older builds answer nothing, and settle the event's own `userChoice` instead). Each is answered as MDN's.
interface InstallPromptEvent extends Event {
    prompt(): Promise<{ outcome?: string; userChoice?: string } | undefined>;
    userChoice?: Promise<{ outcome?: string }>;
}

let installEvent: InstallPromptEvent | null = null;

/** Arms what has to listen from boot. Called once, by rask-api.ts, as the runtime loads. */
export function raskWebArm(win: Window): void {
    win.addEventListener("beforeinstallprompt", e => {
        installEvent = e as InstallPromptEvent;
    });
    win.addEventListener("appinstalled", () => {
        installEvent = null;
    });
}

async function promptInstall(event: InstallPromptEvent): Promise<{ userChoice?: string }> {
    if (event === installEvent) installEvent = null;
    const answer = await event.prompt();
    return { userChoice: answer?.userChoice ?? answer?.outcome ?? (await event.userChoice)?.outcome };
}

/** Trigger.Install's click: shows the kept install prompt, answering "accepted", "dismissed" or "unavailable". */
export async function raskWebInstallPrompt(): Promise<string> {
    if (!installEvent) return "unavailable";
    try {
        return (await promptInstall(installEvent)).userChoice === "accepted" ? "accepted" : "dismissed";
    } catch {
        return "dismissed";
    }
}

// ---- the tables raskWebWalk and __raskWeb.listen consult -----------------------------------------------------------

// Methods, by `Interface.member`: the patch runs in place of the browser's own.
const calls: Record<string, (target: never, args: unknown[]) => unknown> = {
    "WakeLock.request": requestWakeLock,
    "BeforeInstallPromptEvent.prompt": (event: InstallPromptEvent) => promptInstall(event),
};

/** The patch for calling `name` on `target`, if one of its interfaces has one. */
export function raskWebCall(target: object, name: string): ((args: unknown[]) => unknown) | undefined {
    for (const key in calls) {
        const [iface, member] = key.split(".");
        const ctor = (globalThis as Record<string, unknown>)[iface];
        if (member === name && typeof ctor === "function" && target instanceof ctor) {
            return args => calls[key](target as never, args);
        }
    }
    return undefined;
}

/** After a listener is added: hands it what fired before it could listen (the window's install prompt). */
export function raskWebListened(target: EventTarget, type: string, listener: (event: Event) => void): void {
    const kept = installEvent;
    if (type === "beforeinstallprompt" && target === globalThis && kept) {
        setTimeout(() => {
            if (installEvent === kept) listener(kept);
        }, 0);
    }
}
