// Cross-tab mutual exclusion — navigator.locks.
//
// See ./geolocation.ts for the two rules every module in this directory follows.
//
// The lock is held for exactly as long as the promise your callback returns stays pending. That is
// the platform's design and it is a good one: there is no release() to forget, and a lock cannot
// outlive the tab holding it.

export interface LockInfo {
    /** Optional because the platform's own LockInfo is: a lock can be reported without one. */
    name?: string;
    mode?: string;
    clientId?: string;
    /** False for a request still waiting in the queue. */
    held: boolean;
}

export interface LockOptions {
    /** "exclusive" (the default) or "shared". */
    mode?: LockMode;
    /** Fail immediately rather than queue when the lock is already held. */
    ifAvailable?: boolean;
}

export function isSupported(): boolean {
    return typeof navigator !== "undefined" && !!(navigator.locks && navigator.locks.request);
}

/**
 * Hold `name` for the duration of `work`, and return what it returned.
 *
 * With `ifAvailable` and the lock already held, `work` never runs and this resolves NULL — so a
 * caller distinguishes "did the work" from "someone else had it" without a second flag.
 */
export function request<T>(
    name: string,
    work: () => Promise<T>,
    options?: LockOptions): Promise<T | null> {
    const init: globalThis.LockOptions = {mode: (options && options.mode) || "exclusive"};
    if (options && options.ifAvailable) {
        init.ifAvailable = true;
    }

    return navigator.locks.request(name, init, async (lock) => {
        if (!lock) {
            return null; // ifAvailable, and someone else holds it
        }
        return await work();
    }) as Promise<T | null>;
}

/** What is held and what is queued, across every tab of this origin. */
export function query(): Promise<LockInfo[]> {
    if (typeof navigator === "undefined" || !navigator.locks || !navigator.locks.query) {
        return Promise.resolve([]);
    }
    return navigator.locks.query().then((state) => {
        const out: LockInfo[] = [];
        (state.held || []).forEach((l) =>
            out.push({name: l.name, mode: l.mode, clientId: l.clientId, held: true}));
        (state.pending || []).forEach((l) =>
            out.push({name: l.name, mode: l.mode, clientId: l.clientId, held: false}));
        return out;
    });
}
