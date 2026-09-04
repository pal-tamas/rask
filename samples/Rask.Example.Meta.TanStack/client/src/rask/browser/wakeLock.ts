// Keeping the screen awake — the Screen Wake Lock API.
//
// See ./geolocation.ts for the two rules every module in this directory follows.
//
// The part worth having: the browser RELEASES a wake lock whenever the page stops being visible, and
// does not give it back when the user returns. So a recipe left open while the user checks a message
// silently stops working. This re-acquires on `visibilitychange` for every lock still meant to be
// held, which is what makes "held until I release it" true rather than approximately true.

/** A held lock. Release it when the reason for holding it is over. */
export interface WakeLockHandle {
    release(): Promise<void>;
    /** False while the browser has taken it away — it will be re-acquired when the page returns. */
    readonly held: boolean;
}

interface Entry {
    sentinel: WakeLockSentinel;
    released: boolean;
    /** Set once the caller has released it, so the visibility handler stops re-acquiring. */
    done: boolean;
}

const entries = new Set<Entry>();
let boundVisibility = false;

function track(entry: Entry): void {
    entry.sentinel.addEventListener("release", () => {
        entry.released = true;
    });
}

function bindVisibility(): void {
    if (boundVisibility || typeof document === "undefined") {
        return;
    }
    boundVisibility = true;
    document.addEventListener("visibilitychange", async () => {
        if (document.visibilityState !== "visible") {
            return;
        }
        for (const entry of entries) {
            if (entry.done || !entry.released) {
                continue;
            }
            try {
                entry.sentinel = await navigator.wakeLock.request("screen");
                entry.released = false;
                track(entry);
            } catch {
                // Best-effort: the page may no longer have the right to hold one.
            }
        }
    });
}

export function isSupported(): boolean {
    return typeof navigator !== "undefined" && "wakeLock" in navigator;
}

/** Take a wake lock. Rejects where unsupported or refused. */
export async function request(): Promise<WakeLockHandle> {
    bindVisibility();

    const entry: Entry = {
        sentinel: await navigator.wakeLock.request("screen"),
        released: false,
        done: false
    };
    track(entry);
    entries.add(entry);

    return {
        get held() {
            return !entry.released && !entry.done;
        },
        release: async () => {
            if (entry.done) {
                return;
            }
            entry.done = true;
            entries.delete(entry);
            try {
                await entry.sentinel.release();
            } catch {
                // Already released, most likely by the page going hidden.
            }
        }
    };
}
